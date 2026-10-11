using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.Dns;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RegistryBackupRecoveryTests
{
    // Reserved names and documentation RDATA qualify a disposable recovery fixture, not a deployment.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ005", Justification = "xUnit Assert.Single verifies exactly one item; LINQ First would remove the recovery cardinality assertion.")]
    private static T Only<T>(IReadOnlyList<T> items) => Assert.Single(items);

    private static readonly Uri Issuer = new("https://issuer.example/directory");
    private static readonly DnsJournalScope Scope = new("records", new string('A', 64));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentOfflineCopyRetainsAuthorityAndJournalsButRequiresFreshEligibilityAsync(bool revoke)
    {
        using var source = new RegistryStateDirectory();
        using var target = new RegistryStateDirectory();
        using var fixture = new RegistryTestFixture();
        var intent = RegistryTestFixture.Intent();
        var policy = new PolicyRevision(1, "{\"site\":{\"algorithm\":1}}", fixture.Clock.GetUtcNow(), "administrator", "control");
        var mutation = new DnsMutationEntry(Guid.NewGuid(), "svc.site.test", 1, 300,
            Convert.ToBase64String(new byte[] { 192, 0, 2, 7 }), Remove: false, Guid.Empty, ExpectedZoneRevision: 9, "prepared", Revision: 0, Serial: 0, "", "/v2/operations/test");
        var now = fixture.Clock.GetUtcNow();
        var issuerStatus = new AcmeCertificateLifecycleStatus("site", Enabled: true, ["site.test", "*.site.test"], Active: false, "none",
            NotBeforeUtc: null, NotAfterUtc: null, now, now, LastSucceededAtUtc: null, now, now.AddSeconds(30), "failed", "synthetic issuance failure");
        long revision;
        var original = await SqliteRegistryRepository.OpenAsync(source.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (original.ConfigureAwait(true))
        {
            using var controller = new RegistryCoordinator(original, fixture.Availability, fixture.Clock);
            await controller.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            await controller.EnrollAsync(fixture.Grant, "administrator", CancellationToken.None).ConfigureAwait(true);
            await controller.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
            Assert.True(fixture.Availability.SetReadiness(intent.Identity, ready: true, TimeSpan.FromSeconds(120)));
            Assert.True(fixture.Availability.SetPublication(intent.Identity, Publication(controller.State.Revision, fixture.Clock)));
            Assert.True(fixture.Availability.IsEligible(intent.Identity));
            if (revoke) await controller.RevokeNodeAsync("node", "administrator", CancellationToken.None).ConfigureAwait(true);
            revision = controller.State.Revision;
            Assert.True(await original.TryCommitPolicyAsync(0, revision, policy, CancellationToken.None).ConfigureAwait(true));
            await original.CommitDnsJournalAsync(Scope, 0, new DnsJournalState(1, [mutation]), CancellationToken.None).ConfigureAwait(true);
            await new SqliteAcmeCertificateStatusPersistence(original, "site.test", Issuer).UpsertAsync(issuerStatus, CancellationToken.None).ConfigureAwait(true);
        }

        // The only writer has settled. Copy the coherent database/WAL group, never the live locks or SHM.
        var sourceFiles = await CopyOfflineAsync(source.Path, target.Path, revision).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await SqliteRegistryRepository.OpenAsync(target.Path, "other-site", CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);

        var restored = await SqliteRegistryRepository.OpenAsync(target.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (restored.ConfigureAwait(true))
        {
            var availability = new DestinationAvailabilityStore(fixture.Clock);
            using var controller = new RegistryCoordinator(restored, availability, fixture.Clock);
            await controller.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            VerifyAuthority(controller.State, fixture.Grant, intent, revision, revoke);
            await VerifyRetainedRecordsAsync(restored, policy, mutation, issuerStatus).ConfigureAwait(true);
            await VerifyEligibilityAsync(controller, availability, fixture.Clock, intent, fixture.Grant, revision, revoke).ConfigureAwait(true);
        }
        foreach (var (name, hash) in sourceFiles)
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(source.Path, name), CancellationToken.None).ConfigureAwait(true)));
    }

    private static void VerifyAuthority(RegistryState state, NodeGrant grant, InstanceIntent intent, long revision, bool revoke)
    {
        Assert.Equal(revision, state.Revision);
        Assert.Equal(revoke, state.Grants["node"].Revoked);
        Assert.Equal(intent.Identity, state.Instances[intent.Identity.InstanceId].Identity);
        Assert.Equal(revoke, state.IsTombstoned(intent.Identity));
        Assert.Equal(grant.CertificateFingerprint, state.Grants["node"].CertificateFingerprint);
        Assert.Equal(grant.OwnerId, state.Grants["node"].OwnerId);
        Assert.Equal(grant.EndpointAddresses, state.Grants["node"].EndpointAddresses);
    }

    private static async Task<IReadOnlyDictionary<string, byte[]>> CopyOfflineAsync(string source, string target, long revision)
    {
        var audit = await ReadAuditAsync(source).ConfigureAwait(true);
        Assert.Equal(revision, audit.LongLength);
        var hashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in new[] { "registry.sqlite", "registry.sqlite-wal" })
        {
            var path = Path.Combine(source, name);
            if (!File.Exists(path)) continue;
            hashes.Add(name, SHA256.HashData(await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true)));
            File.Copy(path, Path.Combine(target, name));
        }
        Assert.True(hashes.ContainsKey("registry.sqlite"));
        Assert.False(File.Exists(Path.Combine(target, "registry-writer.lock")));
        Assert.False(File.Exists(Path.Combine(target, "registry.sqlite-shm")));
        Assert.Equal(audit, await ReadAuditAsync(target).ConfigureAwait(true));
        return hashes;
    }

    private static async Task VerifyRetainedRecordsAsync(SqliteRegistryRepository restored, PolicyRevision policy,
        DnsMutationEntry mutation, AcmeCertificateLifecycleStatus issuerStatus)
    {
        Assert.Equal(policy, await restored.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(policy, Only(await restored.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true)));
        var journal = await restored.ReadDnsJournalAsync(Scope, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, journal.Revision);
        Assert.Equal(mutation, Only(journal.Entries));
        var item = Only(await new SqliteAcmeCertificateStatusPersistence(restored, "site.test", Issuer).ReadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(issuerStatus.LastFailedAtUtc, item.LastFailedAtUtc);
        Assert.Equal(issuerStatus.NextAttemptNotBeforeUtc, item.NextAttemptNotBeforeUtc);
        Assert.Equal(issuerStatus.LastResult, item.LastResult);
    }

    private static async Task VerifyEligibilityAsync(RegistryCoordinator controller, DestinationAvailabilityStore availability,
        RegistryTimeProvider clock, InstanceIntent intent, NodeGrant grant, long revision, bool revoke)
    {
        Assert.False(availability.IsEligible(intent.Identity));
        Assert.False(availability.SetReadiness(intent.Identity, ready: true, TimeSpan.FromSeconds(120)));
        Assert.False(availability.SetPublication(intent.Identity, Publication(revision, clock)));
        if (revoke)
        {
            await controller.EnrollAsync(grant, "administrator", CancellationToken.None).ConfigureAwait(true);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await controller.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
            Assert.False(availability.IsEligible(intent.Identity));
        }
        else
        {
            await controller.RenewAsync(RegistryTestFixture.Fingerprint, intent.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
            Assert.False(availability.IsEligible(intent.Identity));
            Assert.True(availability.SetReadiness(intent.Identity, ready: true, TimeSpan.FromSeconds(120)));
            Assert.False(availability.IsEligible(intent.Identity));
            Assert.True(availability.SetPublication(intent.Identity, Publication(revision, clock)));
            Assert.True(availability.IsEligible(intent.Identity));
        }
    }

    private static DestinationPublication Publication(long revision, TimeProvider clock) =>
        new(revision, 1, DnsVerified: true, CertificateVerified: true, clock.GetUtcNow().AddMinutes(1)) { Address = new PublishedServiceAddress("svc.site.test", "/") };

    private static async Task<string[]> ReadAuditAsync(string directory)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "registry.sqlite"), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        await using var lifetime = connection.ConfigureAwait(true);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(true);
        var command = connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(true);
        command.CommandText = "SELECT revision, at_utc, operation, actor, subject FROM registry_audit ORDER BY revision";
        var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(true);
        await using var readerLifetime = reader.ConfigureAwait(true);
        var audit = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(true))
            audit.Add(string.Create(CultureInfo.InvariantCulture, $"{reader.GetInt64(0)}|{reader.GetInt64(1)}|{reader.GetString(2)}|{reader.GetString(3)}|{reader.GetString(4)}"));
        return [.. audit];
    }
}
