using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProtectedRegistrySnapshotTests
{
    private const long MaximumBytes = 16 * 1024 * 1024;

    [Fact]
    public async Task ProtectedRoundTripRetainsAllHistoryPrunesOrphansAndReprotectsWithNewKeysAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var path = await InitializeRoleAsync(source.Path, source.Path).ConfigureAwait(true);
        var (history, orphan) = await PopulateHistoryAsync(source.Path, path).ConfigureAwait(true);
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var created = await RegistrySnapshot.CreateProtectedAsync(source.Path, snapshot, "synthetic-site", MaximumBytes, path,
            allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        var escrow = Assert.IsType<PolicyEscrowReceipt>(created.PolicyEscrow);
        Assert.Equal(64, escrow.References.Count);
        Assert.Equal(history.Select(static revision => PolicyPayloadSnapshot.Describe(revision.CanonicalJson)), escrow.References);
        Assert.DoesNotContain(orphan, escrow.References);
        var destination = Path.Combine(parent.Path, "restored");
        var restored = await RegistrySnapshot.RestoreProtectedAsync(snapshot, destination, "synthetic-site", MaximumBytes, created,
            allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(created.RegistryRevision + 1, restored.RegistryRevision);
        Assert.Equal(created.PolicyRevision, restored.PolicyRevision);
        var archive = await PortablePolicyEscrow.LoadAsync(Path.Combine(destination, "policy"), "synthetic-site", allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        using var newEnvironment = new RegistryStateDirectory();
        var newPath = await InitializeRoleAsync(newEnvironment.Path, destination).ConfigureAwait(true);
        var newRole = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(newPath, CancellationToken.None).ConfigureAwait(true);
        await using (newRole.ConfigureAwait(true))
        {
            var payloads = new PortablePolicyPayloadStore(newRole);
            await payloads.RestoreAsync(archive, escrow.References, CancellationToken.None).ConfigureAwait(true);
            var captured = await payloads.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.Throws<InvalidDataException>(() => captured.Resolve(orphan));
            Assert.Equal(63, captured.References.Count);
            var repository = await SqliteRegistryRepository.OpenAsync(destination, "synthetic-site", payloads, CancellationToken.None).ConfigureAwait(true);
            await using (repository.ConfigureAwait(true))
            {
                Assert.Equal(history, await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true));
            }
        }
        await AssertKeysDifferAsync(Path.Combine(source.Path, ".installation.key"), Path.Combine(snapshot, "policy", ".installation.key")).ConfigureAwait(true);
        await AssertKeysDifferAsync(Path.Combine(snapshot, "policy", ".installation.key"), Path.Combine(destination, "policy", ".installation.key")).ConfigureAwait(true);
        await AssertKeysDifferAsync(Path.Combine(destination, "policy", ".installation.key"), Path.Combine(newEnvironment.Path, ".installation.key")).ConfigureAwait(true);
        foreach (var file in new[] { Path.Combine(snapshot, "registry.sqlite"), Path.Combine(snapshot, "policy", ".env"), newPath })
            Assert.DoesNotContain("synthetic-policy-secret", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, CancellationToken.None).ConfigureAwait(true)), StringComparison.Ordinal);
    }

    private static async Task<(IReadOnlyList<PolicyRevision> History, PolicyPayloadReference Orphan)> PopulateHistoryAsync(string state, string path)
    {
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        IReadOnlyList<PolicyRevision> history;
        PolicyPayloadReference orphan;
        await using (role.ConfigureAwait(true))
        {
            var payloads = new PortablePolicyPayloadStore(role);
            var repository = await SqliteRegistryRepository.OpenAsync(state, "synthetic-site", payloads, CancellationToken.None).ConfigureAwait(true);
            await using (repository.ConfigureAwait(true))
            {
                for (var index = 1; index <= 70; index++)
                    Assert.True(await repository.TryCommitPolicyAsync(index - 1, 0, Revision(index), CancellationToken.None).ConfigureAwait(true));
                var previous = (await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true))[0];
                Assert.True(await repository.TryCommitPolicyAsync(70, 0,
                    new PolicyRevision(71, previous.CanonicalJson, DateTimeOffset.UnixEpoch, "operator", "control"), CancellationToken.None).ConfigureAwait(true));
                history = await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true);
                orphan = await payloads.StageAsync("{\"orphan\":true}", [.. history.Select(static revision => PolicyPayloadSnapshot.Describe(revision.CanonicalJson))], CancellationToken.None).ConfigureAwait(true);
            }
        }
        return (history, orphan);
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("key")]
    [InlineData("references")]
    [InlineData("missing-key")]
    [InlineData("foreign-site")]
    [InlineData("unexpected-file")]
    public async Task DamagedOrUnapprovedProtectedImageNeverPublishesAsync(string fault)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var path = await InitializeRoleAsync(source.Path, source.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using (role.ConfigureAwait(true))
        {
            var repository = await SqliteRegistryRepository.OpenAsync(source.Path, "synthetic-site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
            await using (repository.ConfigureAwait(true))
            {
                Assert.True(await repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).ConfigureAwait(true));
            }
        }
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var created = await RegistrySnapshot.CreateProtectedAsync(source.Path, snapshot, "synthetic-site", MaximumBytes, path,
            allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        var escrow = Assert.IsType<PolicyEscrowReceipt>(created.PolicyEscrow);
        if (fault is "archive" or "key")
        {
            var file = Path.Combine(snapshot, "policy", string.Equals(fault, "archive", StringComparison.Ordinal) ? ".env" : ".installation.key");
            var bytes = await File.ReadAllBytesAsync(file, CancellationToken.None).ConfigureAwait(true);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(file, bytes, CancellationToken.None).ConfigureAwait(true);
            CryptographicOperations.ZeroMemory(bytes);
        }
        else if (string.Equals(fault, "references", StringComparison.Ordinal))
        {
            created = created with { PolicyEscrow = escrow with { References = [PolicyPayloadSnapshot.Describe("{\"wrong\":true}")] } };
        }
        else if (string.Equals(fault, "missing-key", StringComparison.Ordinal))
        {
            File.Delete(Path.Combine(snapshot, "policy", ".installation.key"));
        }
        else if (string.Equals(fault, "unexpected-file", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(Path.Combine(snapshot, "policy", "unexpected"), "synthetic", CancellationToken.None).ConfigureAwait(true);
        }

        var destination = Path.Combine(parent.Path, "refused");
        await Assert.ThrowsAsync<InvalidDataException>(() => RegistrySnapshot.RestoreProtectedAsync(snapshot, destination,
            string.Equals(fault, "foreign-site", StringComparison.Ordinal) ? "foreign-site" : "synthetic-site", MaximumBytes, created,
            allowCredentialEscrow: true, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.False(Directory.Exists(destination));
        Assert.Equal([snapshot], Directory.GetDirectories(parent.Path));
        if (string.Equals(fault, "missing-key", StringComparison.Ordinal)) Assert.False(File.Exists(Path.Combine(snapshot, "policy", ".installation.key")));
    }

    [Fact]
    public async Task UnapprovedProtectedExportRefusesBeforeSourceOrDestinationEffectsAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => RegistrySnapshot.CreateProtectedAsync(source.Path, Path.Combine(parent.Path, "refused"),
            "synthetic-site", MaximumBytes, Path.Combine(source.Path, ".env"), allowCredentialEscrow: false, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Empty(Directory.EnumerateFileSystemEntries(source.Path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(parent.Path));
    }

    internal static async Task<string> InitializeRoleAsync(string environment, string state, string siteId = "synthetic-site")
    {
        var path = Path.Combine(environment, ".env");
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(new ApplicationBootstrap { SiteId = siteId, StateDirectory = state }), CancellationToken.None).ConfigureAwait(false);
        return path;
    }

    private static PolicyRevision Revision(int index) => new(index, "{\"credential\":\"synthetic-policy-secret-" + index.ToString(CultureInfo.InvariantCulture) + "\"}",
        DateTimeOffset.FromUnixTimeMilliseconds(index), "operator", "control");

    private static async Task AssertKeysDifferAsync(string left, string right)
    {
        var first = await File.ReadAllBytesAsync(left, CancellationToken.None).ConfigureAwait(false);
        var second = await File.ReadAllBytesAsync(right, CancellationToken.None).ConfigureAwait(false);
        try { Assert.False(CryptographicOperations.FixedTimeEquals(first, second)); }
        finally { CryptographicOperations.ZeroMemory(first); CryptographicOperations.ZeroMemory(second); }
    }
}
