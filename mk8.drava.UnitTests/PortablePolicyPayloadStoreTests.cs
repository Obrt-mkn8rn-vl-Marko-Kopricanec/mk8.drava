using System.Globalization;
using System.Text.Json;
using System.Text;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortablePolicyPayloadStoreTests
{
    [Fact]
    public async Task StagingSurvivesRestartAndOlderSnapshotsKeepTheirOwnedContentsAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using (role.ConfigureAwait(true))
        {
            var store = new PortablePolicyPayloadStore(role);
            var first = await store.StageAsync("{\"value\":\"synthetic-one\"}", [], CancellationToken.None).ConfigureAwait(true);
            var snapshot = await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
            var second = await store.StageAsync("{\"value\":\"synthetic-two\"}", [], CancellationToken.None).ConfigureAwait(true);
            Assert.Equal("{\"value\":\"synthetic-one\"}", snapshot.Resolve(first));
            var current = await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.Equal(second, UpstreamTrustConfigurationTests.Only(current.References));
            Assert.Throws<InvalidDataException>(() => current.Resolve(first));
            Assert.Equal("{\"value\":\"synthetic-two\"}", current.Resolve(second));
            Assert.Equal(second, await store.StageAsync(current.Resolve(second), [second], CancellationToken.None).ConfigureAwait(true));
            Assert.Equal("[redacted]", role.ReadPolicyDocuments().Archive.ToString());
            Assert.Equal("[redacted]", role.ReadPolicyDocuments().ConfiguredSource.ToString());
        }
        var reopened = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var reopenedLifetime = reopened.ConfigureAwait(true);
        var restored = await new PortablePolicyPayloadStore(reopened).ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("{\"value\":\"synthetic-two\"}", restored.Resolve(UpstreamTrustConfigurationTests.Only(restored.References)));
        Assert.Equal("synthetic-site", reopened.Configuration.SiteId);
        var settings = await Store(directory.Path).LoadAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("retained", settings["Unrelated:Value"]);
        Assert.Equal(SecretFileProtectionState.Protected, await Store(directory.Path).GetStateAsync(CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task SeventyStagesRetainAllSixtyFourLiveReferencesAndReuseTheirDigestsAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new PortablePolicyPayloadStore(role);
        var retained = new List<PolicyPayloadReference>();
        for (var index = 1; index <= 70; index++)
        {
            retained.Add(await store.StageAsync(Payload(index, 128), retained, CancellationToken.None).ConfigureAwait(true));
            if (retained.Count > 64) retained.RemoveAt(0);
        }
        var staged = await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(65, staged.References.Count);
        var reused = await store.StageAsync(Payload(70, 128), retained, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(retained[^1], reused);
        var current = await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(64, current.References.Count);
        for (var index = 0; index < retained.Count; index++) Assert.Equal(Payload(index + 7, 128), current.Resolve(retained[index]));
        Assert.Throws<InvalidDataException>(() => current.Resolve(PolicyPayloadSnapshot.Describe(Payload(6, 128))));
    }

    [Fact]
    public async Task SixtyFourMaximalPoliciesAndAStageFitTheProtectedCollectionAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var payloads = Enumerable.Range(1, 64).Select(index => Payload(index, PolicyPayloadReference.MaximumUtf8Bytes))
            .ToDictionary(static text => PolicyPayloadSnapshot.Describe(text).Digest, static text => text, StringComparer.Ordinal);
        var archive = ArchiveFixture(new PolicyPayloadSnapshot("synthetic-site", payloads));
        var path = await InitializeAsync(directory.Path, archive).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new PortablePolicyPayloadStore(role);
        var original = await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
        var staged = await store.StageAsync(Payload(65, PolicyPayloadReference.MaximumUtf8Bytes), original.References, CancellationToken.None).ConfigureAwait(true);
        var current = await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(65, current.References.Count);
        Assert.Equal(Payload(65, PolicyPayloadReference.MaximumUtf8Bytes), current.Resolve(staged));
        foreach (var reference in original.References) Assert.Equal(original.Resolve(reference), current.Resolve(reference));
        Assert.InRange(new FileInfo(path).Length, 4 * 1024 * 1024, InstallationCollectionBounds.MaximumDocumentBytes);
    }

    [Fact]
    public async Task MissingRetainedPayloadAndOversizeInputRejectBeforeAnyWriteAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new PortablePolicyPayloadStore(role);
        var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        var missing = PolicyPayloadSnapshot.Describe("{\"missing\":true}");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageAsync("{}", [missing], CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageAsync(new string('x', PolicyPayloadReference.MaximumUtf8Bytes + 1), [], CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageAsync("{}", [.. Enumerable.Repeat(missing, 65)], CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
        Assert.Empty((await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true)).References);
    }

    [Theory]
    [InlineData("wrong-site")]
    [InlineData("wrong-schema")]
    [InlineData("duplicate-field")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-payload")]
    [InlineData("wrong-digest")]
    [InlineData("padding")]
    [InlineData("noncanonical-bits")]
    [InlineData("invalid-utf8")]
    public async Task DamagedArchiveCannotBeReadOrUsedForStagingAsync(string fault)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var digest = PolicyPayloadSnapshot.Describe("{}").Digest;
        var entry = $"\"{digest}\":\"e30\"";
        var text = fault switch
        {
            "wrong-site" => Envelope("foreign", "1", entry),
            "wrong-schema" => Envelope("synthetic-site", "2", entry),
            "duplicate-field" => "{\"schemaVersion\":1,\"schemaVersion\":1,\"siteId\":\"synthetic-site\",\"payloads\":{}}",
            "unknown-field" => "{\"schemaVersion\":1,\"siteId\":\"synthetic-site\",\"payloads\":{},\"head\":1}",
            "duplicate-payload" => Envelope("synthetic-site", "1", entry + "," + entry),
            "wrong-digest" => Envelope("synthetic-site", "1", $"\"{new string('0', 64)}\":\"e30\""),
            "padding" => Envelope("synthetic-site", "1", $"\"{digest}\":\"e30=\""),
            "noncanonical-bits" => Envelope("synthetic-site", "1", $"\"{digest}\":\"e31\""),
            "invalid-utf8" => Envelope("synthetic-site", "1", $"\"{digest}\":\"__8\""),
            _ => throw new InvalidOperationException("Unknown synthetic archive corruption."),
        };
        var path = await InitializeAsync(directory.Path, new PortablePolicyArchive(text)).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new PortablePolicyPayloadStore(role);
        var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadSnapshotAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageAsync("{}", [], CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public void SnapshotCopiesThePayloadMapAndRejectsInvalidReferenceMetadata()
    {
        var reference = PolicyPayloadSnapshot.Describe("{}");
        var payloads = new Dictionary<string, string>(StringComparer.Ordinal) { [reference.Digest] = "{}" };
        var snapshot = new PolicyPayloadSnapshot("synthetic-site", payloads);
        payloads.Clear();
        Assert.Equal("{}", snapshot.Resolve(reference));
        Assert.Throws<InvalidDataException>(() => snapshot.Resolve(new PolicyPayloadReference(reference.Digest, 3)));
        Assert.Throws<InvalidDataException>(() => new PolicyPayloadReference(new string('a', 64), 2));
        Assert.Throws<InvalidDataException>(() => PolicyPayloadSnapshot.Describe("\ud800\ud800"));
        Assert.Throws<InvalidDataException>(() => new PortablePolicyArchive("\ud800"));
        Assert.Throws<InvalidDataException>(() => new PolicyPayloadSnapshot("synthetic-site", Enumerable.Range(1, 66)
            .Select(index => Payload(index, 32)).Select(static text => new KeyValuePair<string, string>(PolicyPayloadSnapshot.Describe(text).Digest, text))));
    }

    internal static async Task<string> InitializeAsync(string directory, PortablePolicyArchive? archive = null)
    {
        var path = Path.Combine(directory, ".env");
        var settings = new List<SupprocomSecretSetting>
        {
            new("Drava:ApplicationBootstrap", BootstrapFile.Serialize(new ApplicationBootstrap { SiteId = "synthetic-site" })),
            new("Unrelated:Value", "retained"),
        };
        if (archive is not null) settings.Add(new SupprocomSecretSetting(InstallationCollectionBounds.PolicyArchiveKey, archive.Reveal()));
        await PortableFixtureFile.WriteAsync(path, SupprocomSecretDocument.Serialize(settings), CancellationToken.None).ConfigureAwait(false);
        return path;
    }

    internal static SupprocomSecretFileStore Store(string directory) => new(new SupprocomSecretFileOptions
    {
        Directory = directory, Protection = SecretFileProtection.InstallationBoundAesGcm,
    }, "Production");

    // Independent wire fixtures exercise the private production codec through its public storage boundary.
    internal static PortablePolicyArchive ArchiveFixture(PolicyPayloadSnapshot snapshot)
    {
        var payloads = snapshot.References.ToDictionary(static reference => reference.Digest,
            reference => Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshot.Resolve(reference))).TrimEnd('=').Replace('+', '-').Replace('/', '_'), StringComparer.Ordinal);
        return new PortablePolicyArchive(JsonSerializer.Serialize(new { schemaVersion = 1, siteId = snapshot.SiteId, payloads }));
    }

    private static string Payload(int index, int bytes)
    {
        var prefix = $"{{\"id\":{index.ToString(CultureInfo.InvariantCulture)},\"value\":\"";
        return prefix + new string('x', bytes - prefix.Length - 2) + "\"}";
    }

    private static string Envelope(string site, string version, string entries) =>
        "{\"schemaVersion\":" + version + ",\"siteId\":" + JsonSerializer.Serialize(site) + ",\"payloads\":{" + entries + "}}";
}
