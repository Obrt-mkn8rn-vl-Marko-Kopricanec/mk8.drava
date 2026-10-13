using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortableRoleTests
{
    [Fact]
    public async Task MaterialWritesPreserveTheRoleSnapshotAndUnrelatedSettingsAcrossRestartAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var bootstrap = new GatewayBootstrap { SiteId = "configured" };
        var document = SupprocomSecretDocument.Serialize([
            new SupprocomSecretSetting("Drava:GatewayBootstrap", BootstrapFile.Serialize(bootstrap)),
            new SupprocomSecretSetting("Unrelated:Value", "retained"),
        ]);
        await PortableFixtureFile.WriteAsync(path, document).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using (role.ConfigureAwait(true))
        {
            Assert.Equal(0, role.ReadMaterial().Length);
            var material = new SecretBytes(new byte[128]);
            await role.WriteMaterialAsync(material, CancellationToken.None).ConfigureAwait(true);
            Assert.Equal(material, role.ReadMaterial());
            Assert.Equal(BootstrapFile.Serialize(bootstrap), BootstrapFile.Serialize(role.Configuration), StringComparer.Ordinal);
        }
        var restored = await PortableConfiguration.LoadRoleDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var restoredLifetime = restored.ConfigureAwait(true);
        Assert.Equal(new SecretBytes(new byte[128]), restored.ReadMaterial());
        var store = Store(directory.Path);
        var snapshot = await store.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("retained", snapshot["Unrelated:Value"], StringComparer.Ordinal);
        Assert.Equal(SecretFileProtectionState.Protected, await store.GetStateAsync(CancellationToken.None).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalConfigurationOrMaterialChangesRefuseStaleOwnerWritesAsync(bool changeMaterial)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(new GatewayBootstrap { SiteId = "original" })).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        try
        {
            var store = Store(directory.Path);
            var key = changeMaterial ? "Drava:Material:GatewayBootstrap" : "Drava:GatewayBootstrap";
            var value = changeMaterial ? BootstrapFile.Serialize(new SecretBytes(new byte[128]))
                : BootstrapFile.Serialize(new GatewayBootstrap { SiteId = "external" });
            await store.SetAsync(key, value, CancellationToken.None).ConfigureAwait(true);
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
            await Assert.ThrowsAsync<InvalidDataException>(() => role.WriteMaterialAsync(new SecretBytes(new byte[129]), CancellationToken.None).AsTask()).ConfigureAwait(true);
            Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(0, role.ReadMaterial().Length);
            Assert.Equal("original", role.Configuration.SiteId, StringComparer.Ordinal);
        }
        finally
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => role.DisposeAsync().AsTask()).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task CanceledOrOversizeMaterialNeverChangesTheDocumentAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(new GatewayBootstrap())).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => role.WriteMaterialAsync(new SecretBytes(new byte[128]), cancellation.Token).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => role.WriteMaterialAsync(new SecretBytes(new byte[(128 * 1024) + 37]), CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(0, role.ReadMaterial().Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownJoinsAnOutstandingWriteAndRefusesNewAdmissionAsync(bool fail)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var document = BootstrapFile.Serialize(new GatewayBootstrap());
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.SerializeDocument<GatewayBootstrap>(document)).ConfigureAwait(true);
        var initialized = await PortableConfiguration.LoadRoleDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using (initialized.ConfigureAwait(true))
        {
            Assert.Equal(0, initialized.ReadMaterial().Length);
        }
        var updater = new ControlledUpdater(document, fail);
        var role = new PortableRole<GatewayBootstrap>(new GatewayBootstrap(), updater, path, document,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Drava:GatewayBootstrap"] = document });
        var material = new SecretBytes(new byte[128]);
        var write = role.WriteMaterialAsync(material, CancellationToken.None).AsTask();
        try
        {
            await updater.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(true);
            Assert.Equal(0, role.ReadMaterial().Length);
            await Assert.ThrowsAsync<InvalidOperationException>(() => role.WriteMaterialAsync(material, CancellationToken.None).AsTask()).ConfigureAwait(true);
            var firstShutdown = role.DisposeAsync().AsTask();
            var secondShutdown = role.DisposeAsync().AsTask();
            Assert.False(firstShutdown.IsCompleted);
            Assert.False(secondShutdown.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => role.WriteMaterialAsync(material, CancellationToken.None).AsTask()).ConfigureAwait(true);
            updater.Release.TrySetResult();
            if (fail)
            {
                IOException? failure = null;
                try { await Task.WhenAll(write, firstShutdown, secondShutdown).ConfigureAwait(true); }
                catch (IOException exception) { failure = exception; }
                Assert.NotNull(failure);
                Assert.IsType<IOException>(Assert.IsType<AggregateException>(write.Exception).InnerException);
                Assert.IsType<IOException>(Assert.IsType<AggregateException>(firstShutdown.Exception).InnerException);
                Assert.IsType<IOException>(Assert.IsType<AggregateException>(secondShutdown.Exception).InnerException);
                Assert.Null(updater.Committed);
            }
            else
            {
                await Task.WhenAll(write, firstShutdown, secondShutdown).ConfigureAwait(true);
                var committed = Assert.IsType<IReadOnlyList<SupprocomSecretSetting>>(updater.Committed, exactMatch: false);
                Assert.Contains(committed, setting => string.Equals(setting.Key, "Drava:Material:GatewayBootstrap", StringComparison.Ordinal)
                    && string.Equals(setting.Value, BootstrapFile.Serialize(material), StringComparison.Ordinal));
            }
        }
        finally
        {
            updater.Release.TrySetResult();
            if (fail) await Assert.ThrowsAsync<IOException>(() => role.DisposeAsync().AsTask()).ConfigureAwait(true);
            else await role.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static SupprocomSecretFileStore Store(string directory) => new(new SupprocomSecretFileOptions
    {
        Directory = directory, Protection = SecretFileProtection.InstallationBoundAesGcm,
    }, "Production");

    private sealed class ControlledUpdater(string document, bool fail) : ISecretDocumentUpdater
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<SupprocomSecretSetting>? Committed { get; private set; }

        public async Task UpdateDocumentAsync(Func<IReadOnlyList<SupprocomSecretSetting>, IReadOnlyList<SupprocomSecretSetting>> update,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (fail) throw new IOException("Controlled material write failure.");
            Committed = update([new SupprocomSecretSetting("Drava:GatewayBootstrap", document)]);
        }
    }
}
