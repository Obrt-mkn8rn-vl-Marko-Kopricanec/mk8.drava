using System.Text;
using System.Text.Json;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortableNoConfPolicySourceTests
{
    [Fact]
    public async Task ProtectedSourceObservesOwnedRefreshAndIgnoresRawPolicyFilesAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(directory.Path, directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var source = new PortableNoConfPolicySource(role);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "noconf.json"), "{broken", CancellationToken.None).ConfigureAwait(true);
        Assert.Null(await source.ReadAsync(CancellationToken.None).ConfigureAwait(true));
        await SetSourceAsync(directory.Path, PortableNoConfPolicySource.Encode(NoConfPolicyFile.Parse("{\"site\":{\"algorithm\":\"least-active\"}}"), "synthetic-site").Reveal()).ConfigureAwait(true);
        var captured = Assert.IsType<NoConfPolicy>(await source.ReadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(BalancingAlgorithm.LeastActive, captured.Site.Algorithm);
        Assert.DoesNotContain("least-active", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true)), StringComparison.Ordinal);
        Assert.True(role.ReadPolicyDocuments().Archive.IsEmpty);
    }

    [Theory]
    [InlineData("wrong-site")]
    [InlineData("wrong-schema")]
    [InlineData("duplicate-field")]
    [InlineData("unknown-field")]
    [InlineData("padding")]
    [InlineData("noncanonical-bits")]
    [InlineData("invalid-utf8")]
    [InlineData("unknown-policy-field")]
    public async Task InvalidEnvelopeOrPolicyIsRejectedAndAHealthyRefreshCanFollowAsync(string fault)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(directory.Path, directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var source = new PortableNoConfPolicySource(role);
        var envelope = fault switch
        {
            "wrong-site" => Envelope("foreign-site", "1", "e30"),
            "wrong-schema" => Envelope("synthetic-site", "2", "e30"),
            "duplicate-field" => "{\"schemaVersion\":1,\"schemaVersion\":1,\"siteId\":\"synthetic-site\",\"policy\":\"e30\"}",
            "unknown-field" => "{\"schemaVersion\":1,\"siteId\":\"synthetic-site\",\"policy\":\"e30\",\"head\":1}",
            "padding" => Envelope("synthetic-site", "1", "e30="),
            "noncanonical-bits" => Envelope("synthetic-site", "1", "e31"),
            "invalid-utf8" => Envelope("synthetic-site", "1", "__8"),
            "unknown-policy-field" => Envelope("synthetic-site", "1", Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"unknown\":true}")).TrimEnd('=').Replace('+', '-').Replace('/', '_')),
            _ => throw new InvalidOperationException("Unknown synthetic policy source fault."),
        };
        await SetSourceAsync(directory.Path, envelope).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        await SetSourceAsync(directory.Path, PortableNoConfPolicySource.Encode(new NoConfPolicy(), "synthetic-site").Reveal()).ConfigureAwait(true);
        _ = await source.ReadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task ControlPrecedenceExplicitImportAndRollbackUseProtectedHistoryAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(directory.Path, directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var runtime = new PolicyRuntime(repository, directory.Path, source: new PortableNoConfPolicySource(role));
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        await runtime.StartAsync().ConfigureAwait(true);
        await runtime.WaitAsync(static view => view.AppliedRevision == 1).ConfigureAwait(true);
        Assert.True(await runtime.Reconciler.UpdatePolicyAsync(1, "{\"site\":{\"algorithm\":\"least-active\"}}", importFile: false, CancellationToken.None).ConfigureAwait(true));
        await SetSourceAsync(directory.Path, "{broken").ConfigureAwait(true);
        var controlled = await runtime.Reconciler.ReadPolicyViewAsync(includeHistory: true, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, controlled.Accepted?.Revision);
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Reconciler.UpdatePolicyAsync(2, json: null, importFile: true, CancellationToken.None).AsTask()).ConfigureAwait(true);
        await SetSourceAsync(directory.Path, PortableNoConfPolicySource.Encode(NoConfPolicyFile.Parse("{\"site\":{\"algorithm\":\"weighted-round-robin\"}}"), "synthetic-site").Reveal()).ConfigureAwait(true);
        Assert.True(await runtime.Reconciler.UpdatePolicyAsync(2, json: null, importFile: true, CancellationToken.None).ConfigureAwait(true));
        Assert.True(await runtime.Reconciler.RollbackPolicyAsync(3, 1, CancellationToken.None).ConfigureAwait(true));
        var rolledBack = await runtime.WaitAsync(static view => view.AppliedRevision == 4).ConfigureAwait(true);
        Assert.Equal("control", rolledBack.Accepted?.Source);
        Assert.Equal(rolledBack.History[^1].Digest, rolledBack.Accepted?.Digest);
        Assert.Equal(4, rolledBack.History.Count);
        Assert.False(File.Exists(Path.Combine(directory.Path, "noconf.json")));
    }

    internal static async Task SetSourceAsync(string directory, string encoded)
    {
        var store = PortablePolicyPayloadStoreTests.Store(directory);
        await store.UpdateDocumentAsync(settings =>
        {
            var replacement = settings.Where(static setting => !string.Equals(setting.Key, InstallationCollectionBounds.PolicySourceKey, StringComparison.OrdinalIgnoreCase)).ToList();
            replacement.Add(new SupprocomSecretSetting(InstallationCollectionBounds.PolicySourceKey, encoded));
            return replacement;
        }, CancellationToken.None).ConfigureAwait(false);
    }

    [Fact]
    public async Task AbsentProtectedInputRetainsAcceptedLegacyPolicyAndExplicitDefaultCanReplaceItAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(directory.Path, directory.Path).ConfigureAwait(true);
        var legacy = await SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", CancellationToken.None).ConfigureAwait(true);
        var original = new PolicyRevision(1, NoConfPolicyFile.Encode(NoConfPolicyFile.Parse("{\"site\":{\"algorithm\":\"least-active\"}}")), DateTimeOffset.UnixEpoch, "file-watcher", "file");
        await using (legacy.ConfigureAwait(true))
        {
            Assert.True(await legacy.TryCommitPolicyAsync(0, 0, original, CancellationToken.None).ConfigureAwait(true));
        }
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var runtime = new PolicyRuntime(repository, directory.Path, source: new PortableNoConfPolicySource(role));
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        await runtime.StartAsync().ConfigureAwait(true);
        var retained = await runtime.WaitAsync(static view => view.AppliedRevision == 1).ConfigureAwait(true);
        Assert.Equal(original, retained.Accepted);
        Assert.Equal(original, UpstreamTrustConfigurationTests.Only(retained.History));
        await SetSourceAsync(directory.Path, PortableNoConfPolicySource.Encode(new NoConfPolicy(), "synthetic-site").Reveal()).ConfigureAwait(true);
        var reset = await runtime.WaitAsync(static view => view.AppliedRevision == 2).ConfigureAwait(true);
        Assert.NotEqual(original.Digest, reset.Accepted?.Digest, StringComparer.Ordinal);
        Assert.Equal(2, reset.History.Count);
        Assert.Equal(original, reset.History[^1]);
    }

    private static string Envelope(string site, string version, string encoded) =>
        "{\"schemaVersion\":" + version + ",\"siteId\":" + JsonSerializer.Serialize(site) + ",\"policy\":" + JsonSerializer.Serialize(encoded) + "}";
}
