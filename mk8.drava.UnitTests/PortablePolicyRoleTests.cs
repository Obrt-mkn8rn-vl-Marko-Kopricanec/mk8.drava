using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortablePolicyRoleTests
{
    [Fact]
    public async Task QueueCapacityRefusesAdditionalAdmissionAndDrainsEveryAcceptedWriteAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var store = PortablePolicyPayloadStoreTests.Store(directory.Path);
        var snapshot = await store.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        var updater = new ControlledUpdater(store, fail: false);
        var role = new PortableRole<ApplicationBootstrap>(new ApplicationBootstrap { SiteId = "synthetic-site" }, updater, path,
            snapshot["Drava:ApplicationBootstrap"], snapshot);
        try
        {
            var material = role.WriteMaterialAsync(new SecretBytes(new byte[128]), CancellationToken.None).AsTask();
            await updater.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(true);
            var writes = new List<Task>(8);
            for (var index = 0; index < 8; index++)
                writes.Add(role.WritePolicyArchiveAsync(PortablePolicyArchive.Empty, PortablePolicyArchive.Empty, CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => role.WritePolicyArchiveAsync(PortablePolicyArchive.Empty, PortablePolicyArchive.Empty, CancellationToken.None).AsTask()).ConfigureAwait(true);
            var close = role.DisposeAsync().AsTask();
            Assert.False(close.IsCompleted);
            updater.Release.TrySetResult();
            await Task.WhenAll([.. writes, material, close]).ConfigureAwait(true);
            Assert.Equal(9, updater.Writes);
        }
        finally
        {
            updater.Release.TrySetResult();
            await role.DisposeAsync().ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaterialAndPolicyWritesShareShutdownAndSettleEveryAdmittedOperationAsync(bool fail)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var store = PortablePolicyPayloadStoreTests.Store(directory.Path);
        var snapshot = await store.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        var updater = new ControlledUpdater(store, fail);
        var role = new PortableRole<ApplicationBootstrap>(new ApplicationBootstrap { SiteId = "synthetic-site" }, updater, path,
            snapshot["Drava:ApplicationBootstrap"], snapshot);
        try
        {
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
            var material = role.WriteMaterialAsync(new SecretBytes(new byte[128]), CancellationToken.None).AsTask();
            await updater.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(true);
            var reference = PolicyPayloadSnapshot.Describe("{}");
            var archive = PortablePolicyPayloadStoreTests.ArchiveFixture(new PolicyPayloadSnapshot("synthetic-site",
                [new KeyValuePair<string, string>(reference.Digest, "{}")]));
            var policy = role.WritePolicyArchiveAsync(PortablePolicyArchive.Empty, archive, CancellationToken.None).AsTask();
            var firstClose = role.DisposeAsync().AsTask();
            var secondClose = role.DisposeAsync().AsTask();
            Assert.False(material.IsCompleted);
            Assert.False(policy.IsCompleted);
            Assert.False(firstClose.IsCompleted);
            Assert.False(secondClose.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => role.WriteMaterialAsync(new SecretBytes(new byte[128]), CancellationToken.None).AsTask()).ConfigureAwait(true);
            updater.Release.TrySetResult();
            if (fail)
            {
                try { await Task.WhenAll(material, policy, firstClose, secondClose).ConfigureAwait(true); }
                catch (IOException) { }
                AssertJoinedFailures([material, policy, firstClose, secondClose]);
                Assert.Equal(1, updater.Writes);
                Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
            }
            else
            {
                await Task.WhenAll(material, policy, firstClose, secondClose).ConfigureAwait(true);
                Assert.Equal(2, updater.Writes);
                await AssertPersistedAsync(path, reference).ConfigureAwait(true);
            }
        }
        finally
        {
            updater.Release.TrySetResult();
            if (fail) await Assert.ThrowsAsync<IOException>(() => role.DisposeAsync().AsTask()).ConfigureAwait(true);
            else await role.DisposeAsync().ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task SuccessfulArchiveAndLaterMaterialWritesCompleteTheirDurableBarriersAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var store = PortablePolicyPayloadStoreTests.Store(directory.Path);
        var snapshot = await store.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        var synchronized = new List<string>();
        var role = new PortableRole<ApplicationBootstrap>(new ApplicationBootstrap { SiteId = "synthetic-site" }, store, path,
            snapshot["Drava:ApplicationBootstrap"], snapshot, (file, minimum, maximum) =>
            {
                InstallationDocumentGuard.Synchronize(file, minimum, maximum);
                synchronized.Add(Path.GetFileName(file));
            });
        await using var roleLifetime = role.ConfigureAwait(true);
        _ = await new PortablePolicyPayloadStore(role).StageAsync("{}", [], CancellationToken.None).ConfigureAwait(true);
        Assert.Collection(synchronized, static file => Assert.Equal(".env", file), static file => Assert.Equal(".installation.key", file));
        await role.WriteMaterialAsync(new SecretBytes(new byte[128]), CancellationToken.None).ConfigureAwait(true);
        Assert.Collection(synchronized, static file => Assert.Equal(".env", file), static file => Assert.Equal(".installation.key", file),
            static file => Assert.Equal(".env", file), static file => Assert.Equal(".installation.key", file));
    }

    [Fact]
    public async Task CanceledQueuedArchiveNeverEntersTheProviderAndShutdownObservesCancellationAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var store = PortablePolicyPayloadStoreTests.Store(directory.Path);
        var snapshot = await store.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        var updater = new ControlledUpdater(store, fail: false);
        using var cancellation = new CancellationTokenSource();
        var role = new PortableRole<ApplicationBootstrap>(new ApplicationBootstrap { SiteId = "synthetic-site" }, updater, path,
            snapshot["Drava:ApplicationBootstrap"], snapshot);
        var verified = false;
        OperationCanceledException? shutdown = null;
        try
        {
            await using (role.ConfigureAwait(true))
            {
                try
                {
                    var material = role.WriteMaterialAsync(new SecretBytes(new byte[128]), CancellationToken.None).AsTask();
                    await updater.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(true);
                    var policy = role.WritePolicyArchiveAsync(PortablePolicyArchive.Empty, new PortablePolicyArchive("{}"), cancellation.Token).AsTask();
                    await cancellation.CancelAsync().ConfigureAwait(true);
                    updater.Release.TrySetResult();
                    await material.ConfigureAwait(true);
                    try { await policy.ConfigureAwait(true); }
                    catch (OperationCanceledException) { }
                    Assert.True(policy.IsCanceled);
                    Assert.Equal(1, updater.Writes);
                    Assert.True(role.ReadPolicyDocuments().Archive.IsEmpty);
                    await role.WriteMaterialAsync(new SecretBytes(new byte[129]), CancellationToken.None).ConfigureAwait(true);
                    Assert.Equal(2, updater.Writes);
                    Assert.Equal(129, role.ReadMaterial().Length);
                    verified = true;
                }
                finally { updater.Release.TrySetResult(); }
            }
        }
        catch (OperationCanceledException exception) when (verified) { shutdown = exception; }
        Assert.NotNull(shutdown);
        Assert.Equal(cancellation.Token, shutdown.CancellationToken);
    }

    [Fact]
    public async Task FailedDurabilityDoesNotPublishOrPermitFurtherWritesAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var store = PortablePolicyPayloadStoreTests.Store(directory.Path);
        var snapshot = await store.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        var role = new PortableRole<ApplicationBootstrap>(new ApplicationBootstrap { SiteId = "synthetic-site" }, store, path,
            snapshot["Drava:ApplicationBootstrap"], snapshot, static (_, _, _) => throw new IOException("Controlled durability failure."));
        var verified = false;
        IOException? shutdown = null;
        IOException? writeFailure = null;
        try
        {
            await using (role.ConfigureAwait(true))
            {
                writeFailure = await Assert.ThrowsAsync<IOException>(() => new PortablePolicyPayloadStore(role).StageAsync("{}", [], CancellationToken.None).AsTask()).ConfigureAwait(true);
                Assert.True(role.ReadPolicyDocuments().Archive.IsEmpty);
                await Assert.ThrowsAsync<IOException>(() => role.WriteMaterialAsync(new SecretBytes(new byte[128]), CancellationToken.None).AsTask()).ConfigureAwait(true);
                var reopened = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
                await using var reopenedLifetime = reopened.ConfigureAwait(true);
                var staged = await new PortablePolicyPayloadStore(reopened).ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
                Assert.Equal("{}", staged.Resolve(PolicyPayloadSnapshot.Describe("{}")));
                verified = true;
            }
        }
        catch (IOException exception) when (verified) { shutdown = exception; }
        Assert.NotNull(shutdown);
        Assert.Same(writeFailure, shutdown);
    }

    [Fact]
    public async Task StaleArchiveSnapshotRejectsBeforeChangingCiphertextAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new PortablePolicyPayloadStore(role);
        var reference = await store.StageAsync("{}", [], CancellationToken.None).ConfigureAwait(true);
        var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => role.WritePolicyArchiveAsync(PortablePolicyArchive.Empty, PortablePolicyArchive.Empty, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(reference, await store.StageAsync("{}", [reference], CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ExpandedPhysicalCollectionDoesNotExpandTheOrdinarySettingsQuotaAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        try
        {
            await PortablePolicyPayloadStoreTests.Store(directory.Path).SetAsync("Unrelated:Value", new string('x', 4 * 1024 * 1024), CancellationToken.None).ConfigureAwait(true);
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
            await Assert.ThrowsAsync<InvalidDataException>(() => role.WriteMaterialAsync(new SecretBytes(new byte[128]), CancellationToken.None).AsTask()).ConfigureAwait(true);
            Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(0, role.ReadMaterial().Length);
        }
        finally { await Assert.ThrowsAsync<InvalidDataException>(() => role.DisposeAsync().AsTask()).ConfigureAwait(true); }
    }

    [Fact]
    public async Task ExactEncodedQuotaRejectsEscapingExpansionBeforeInstallAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        try
        {
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
            var largeEscaped = new PortablePolicyArchive(new string('\u0001', 6 * 1024 * 1024));
            await Assert.ThrowsAsync<InvalidDataException>(() => role.WritePolicyArchiveAsync(PortablePolicyArchive.Empty, largeEscaped, CancellationToken.None).AsTask()).ConfigureAwait(true);
            Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
            Assert.True(role.ReadPolicyDocuments().Archive.IsEmpty);
        }
        finally { await Assert.ThrowsAsync<InvalidDataException>(() => role.DisposeAsync().AsTask()).ConfigureAwait(true); }
    }

    [Fact]
    public async Task RefreshInitiallyCapturesOwnedArchiveThenOnlyAdoptsConfiguredSourceAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = PortablePolicyPayloadStoreTests.Store(directory.Path);
        var reference = PolicyPayloadSnapshot.Describe("{}");
        var archive = PortablePolicyPayloadStoreTests.ArchiveFixture(new PolicyPayloadSnapshot("synthetic-site", [new KeyValuePair<string, string>(reference.Digest, "{}")]));
        await store.SetAsync(InstallationCollectionBounds.PolicyArchiveKey, archive.Reveal(), CancellationToken.None).ConfigureAwait(true);
        var captured = await role.RefreshPolicyDocumentsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(archive, captured.Archive);
        await store.SetAsync(InstallationCollectionBounds.PolicySourceKey, "synthetic-source", CancellationToken.None).ConfigureAwait(true);
        var refreshed = await role.RefreshPolicyDocumentsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("synthetic-source", refreshed.ConfiguredSource.Reveal());
        Assert.Equal(archive, refreshed.Archive);
        Assert.True(captured.ConfiguredSource.IsEmpty);
        Assert.Equal("synthetic-site", role.Configuration.SiteId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshRefusesExternallyChangedMaterialOrArchiveAfterOwnershipAsync(bool material)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        try
        {
            _ = await role.RefreshPolicyDocumentsAsync(CancellationToken.None).ConfigureAwait(true);
            await PortablePolicyPayloadStoreTests.Store(directory.Path).SetAsync(material ? "Drava:Material:ApplicationBootstrap" : InstallationCollectionBounds.PolicyArchiveKey,
                material ? BootstrapFile.Serialize(new SecretBytes(new byte[128])) : "{}", CancellationToken.None).ConfigureAwait(true);
            await Assert.ThrowsAsync<InvalidDataException>(() => role.RefreshPolicyDocumentsAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
            Assert.True(role.ReadPolicyDocuments().Archive.IsEmpty);
            Assert.Equal(0, role.ReadMaterial().Length);
        }
        finally { await Assert.ThrowsAsync<InvalidDataException>(() => role.DisposeAsync().AsTask()).ConfigureAwait(true); }
    }

    private static void AssertJoinedFailures(IReadOnlyList<Task> operations)
    {
        foreach (var operation in operations)
        {
            Assert.True(operation.IsFaulted);
            Assert.IsType<IOException>(Assert.IsType<AggregateException>(operation.Exception).InnerException);
        }
    }

    private static async Task AssertPersistedAsync(string path, PolicyPayloadReference reference)
    {
        var reopened = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(false);
        await using var reopenedLifetime = reopened.ConfigureAwait(false);
        Assert.Equal(new SecretBytes(new byte[128]), reopened.ReadMaterial());
        Assert.Equal("{}", (await new PortablePolicyPayloadStore(reopened).ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(false)).Resolve(reference));
        var settings = await PortablePolicyPayloadStoreTests.Store(Path.GetDirectoryName(path)!).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal("retained", settings["Unrelated:Value"]);
    }

    private sealed class ControlledUpdater(ISecretDocumentUpdater store, bool fail) : ISecretDocumentUpdater
    {
        private int _writes;
        internal int Writes => _writes;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task UpdateDocumentAsync(Func<IReadOnlyList<SupprocomSecretSetting>, IReadOnlyList<SupprocomSecretSetting>> update, CancellationToken cancellationToken = default)
        {
            if (++_writes == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (fail) throw new IOException("Controlled provider failure.");
            await store.UpdateDocumentAsync(update, cancellationToken).ConfigureAwait(false);
        }
    }
}
