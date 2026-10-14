using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.UnitTests;

public sealed class PortableTemplateRefusalTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollectionDisappearanceAfterAdmissionNeverActivatesASeparateTemplateAsync(bool truncate)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var templatePath = Path.Combine(directory.Path, ".env.template");
        var bootstrap = new ApplicationBootstrap { SiteId = "configured" };
        var document = PortableConfiguration.Serialize(bootstrap);
        await PortableFixtureFile.WriteAsync(path, document).ConfigureAwait(true);
        await PortableFixtureFile.WriteAsync(templatePath, document).ConfigureAwait(true);
        var prepared = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await prepared.DisposeAsync().ConfigureAwait(true);
        var keyPath = Path.Combine(directory.Path, ".installation.key");
        var key = await File.ReadAllBytesAsync(keyPath, CancellationToken.None).ConfigureAwait(true);
        var template = await File.ReadAllBytesAsync(templatePath, CancellationToken.None).ConfigureAwait(true);
        var store = PortableConfiguration.CreateStore(path, CancellationToken.None);
        var snapshot = await store.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        var reader = new DisappearingCollectionStore(store, path, truncate);
        var role = new PortableRole<ApplicationBootstrap>(bootstrap, reader, path, BootstrapFile.Serialize(bootstrap), snapshot);
        var verified = false;
        Exception? failure = null;
        Exception? shutdown = null;
        try
        {
            await using (role.ConfigureAwait(true))
            {
                failure = await Record.ExceptionAsync(() => role.RefreshManualConfigurationAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
                output.WriteLine("template_refusal: truncate={0}; reads={1}; failure={2}; active_exists={3}; active_bytes={4}",
                    truncate, reader.Reads, failure?.GetType().Name, File.Exists(path), File.Exists(path) ? new FileInfo(path).Length : -1);
                Assert.NotNull(failure);
                Assert.Equal(1, reader.Reads);
                if (truncate)
                {
                    Assert.Equal("InvalidTemplate", Assert.IsType<SupprocomSecretsException>(failure).Code, StringComparer.Ordinal);
                    Assert.Empty(await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
                }
                else
                {
                    Assert.IsType<IOException>(failure, exactMatch: false);
                    Assert.False(File.Exists(path));
                }
                Assert.Equal(key, await File.ReadAllBytesAsync(keyPath, CancellationToken.None).ConfigureAwait(true));
                Assert.Equal(template, await File.ReadAllBytesAsync(templatePath, CancellationToken.None).ConfigureAwait(true));
                Assert.True(role.ReadManualConfiguration().IsEmpty);
                Assert.True(role.ReadPolicyDocuments().Archive.IsEmpty);
                Assert.Equal("configured", role.Configuration.SiteId, StringComparer.Ordinal);
                verified = true;
            }
        }
        catch (Exception exception) when (verified && exception is IOException or SupprocomSecretsException) { shutdown = exception; }
        Assert.Same(failure, shutdown);
    }

    private sealed class DisappearingCollectionStore(SupprocomSecretFileStore store, string path, bool truncate) : ISecretStore, ISecretDocumentUpdater
    {
        internal int Reads { get; private set; }

        public Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            // PortableRole has already required the active document. Remove it immediately before the real provider reads it.
            if (truncate) File.WriteAllBytes(path, []);
            else File.Delete(path);
            return store.LoadAsync(cancellationToken);
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => store.SetAsync(key, value, cancellationToken);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => store.DeleteAsync(key, cancellationToken);
        public Task UpdateDocumentAsync(Func<IReadOnlyList<SupprocomSecretSetting>, IReadOnlyList<SupprocomSecretSetting>> update,
            CancellationToken cancellationToken = default) => store.UpdateDocumentAsync(update, cancellationToken);
    }
}
