using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.Dns;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.DAL.Storage;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RegistrySnapshotTests
{
    private const long MaximumBytes = 16 * 1024 * 1024;
    // Reserved issuer/domain and documentation RDATA are synthetic recovery fixtures.
    private static readonly Uri Issuer = new("https://issuer.example/directory");
    private static readonly DnsJournalScope Scope = new("records", new string('A', 64));

    [Fact]
    public async Task RestoreRetainsPolicyAndOperationIdentityButClosesEnrollmentAndRecordedBootsAsync()
    {
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        using var fixture = new RegistryTestFixture();
        if (!await RequireSupportedProfileAsync(source.Path, parent.Path).ConfigureAwait(true)) return;
        var intent = RegistryTestFixture.Intent();
        var mutation = await InitializeAuthorityAsync(source.Path, fixture, intent).ConfigureAwait(true);
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var receipt = await RegistrySnapshot.CreateAsync(source.Path, snapshot, "site", MaximumBytes, CancellationToken.None).ConfigureAwait(true);
        var destination = Path.Combine(parent.Path, "restored");
        await RegistrySnapshot.RestoreAsync(snapshot, destination, "site", MaximumBytes, receipt.DatabaseSha256, CancellationToken.None).ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(destination, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        var availability = new DestinationAvailabilityStore(fixture.Clock);
        using var controller = new RegistryCoordinator(repository, availability, fixture.Clock);
        await controller.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.True(controller.State.Grants["node"].Revoked);
        Assert.Equal(fixture.Grant.OwnerId, controller.State.Grants["node"].OwnerId);
        Assert.True(controller.State.Instances[intent.Identity.InstanceId].Draining);
        Assert.True(controller.State.IsTombstoned(intent.Identity));
        Assert.False(availability.IsEligible(intent.Identity));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await controller.RenewAsync(RegistryTestFixture.Fingerprint, intent.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        await controller.EnrollAsync(fixture.Grant, "administrator", CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await controller.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        await VerifyJournalAndPolicyAsync(repository, mutation, fixture.Clock).ConfigureAwait(true);
    }

    [Fact]
    public async Task SnapshotIncludesCommittedWalAndRestoresOnlyIntoNewPrivateStateAsync()
    {
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var destination = Path.Combine(parent.Path, "restored");
        if (!OperatingSystem.IsLinux())
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await RegistrySnapshot.CreateAsync(source.Path, snapshot, "site", MaximumBytes, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
            return;
        }
        await InitializeAsync(source.Path).ConfigureAwait(true);
        var observer = ReadOnlyConnection(source.Path);
        await using var observerLifetime = observer.ConfigureAwait(true);
        await observer.OpenAsync(CancellationToken.None).ConfigureAwait(true);
        // The provider's async overload cannot select a deferred read transaction.
#pragma warning disable CA1849
        var pinnedRead = observer.BeginTransaction(deferred: true);
#pragma warning restore CA1849
        await using var pinnedReadLifetime = pinnedRead.ConfigureAwait(true);
        await PinReadAsync(observer, pinnedRead).ConfigureAwait(true);
        var original = await SqliteRegistryRepository.OpenAsync(source.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (original.ConfigureAwait(true))
        {
            await original.CommitAsync(1, new RegistryState(2, RegistryState.Empty.Grants, RegistryState.Empty.Instances, []),
                new RegistryAudit(DateTimeOffset.UnixEpoch, "fixture", "administrator", "site"), CancellationToken.None).ConfigureAwait(true);
        }
        Assert.True(new FileInfo(Path.Combine(source.Path, "registry.sqlite-wal")).Length > 0);
        var created = await RegistrySnapshot.CreateAsync(source.Path, snapshot, "site", MaximumBytes, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, created.RegistryRevision);
        await VerifyStandaloneAsync(snapshot, created.DatabaseSha256).ConfigureAwait(true);
        var restored = await RegistrySnapshot.RestoreAsync(snapshot, destination, "site", MaximumBytes, created.DatabaseSha256, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(created.RegistryRevision + 1, restored.RegistryRevision);
        await VerifyStandaloneAsync(destination, restored.DatabaseSha256).ConfigureAwait(true);
        await VerifyStandaloneAsync(snapshot, created.DatabaseSha256, allowLocks: true).ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(destination, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        Assert.Equal(3, (await repository.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveApplicationOrRegistryWriterRefusesExportWithoutPublishingAsync(bool application)
    {
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        if (!await RequireSupportedProfileAsync(source.Path, parent.Path).ConfigureAwait(true)) return;
        await InitializeAsync(source.Path).ConfigureAwait(true);
        var destination = Path.Combine(parent.Path, "refused");
        var lease = PrivateOwnerFile.AcquireLease(Path.Combine(source.Path, application ? "application.lock" : "registry-writer.lock"));
        await using var leaseLifetime = lease.ConfigureAwait(true);
        await Assert.ThrowsAsync<IOException>(async () => await RegistrySnapshot.CreateAsync(source.Path, destination, "site", MaximumBytes, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Empty(Directory.EnumerateFileSystemEntries(parent.Path));
    }

    [Fact]
    public async Task CancellationBudgetAndExistingDestinationNeverPublishOrOverwriteAsync()
    {
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        if (!await RequireSupportedProfileAsync(source.Path, parent.Path).ConfigureAwait(true)) return;
        await InitializeAsync(source.Path).ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await RegistrySnapshot.CreateAsync(source.Path, Path.Combine(parent.Path, "cancelled"), "site", MaximumBytes, cancellation.Token).ConfigureAwait(true)).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await RegistrySnapshot.CreateAsync(source.Path, Path.Combine(parent.Path, "oversized"), "site", maximumBytes: 4096, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Empty(Directory.EnumerateFileSystemEntries(parent.Path));
        var existing = Path.Combine(parent.Path, "existing");
        Directory.CreateDirectory(existing);
        var marker = Path.Combine(existing, "owner.txt");
        await File.WriteAllTextAsync(marker, "synthetic owner content", CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<IOException>(async () => await RegistrySnapshot.CreateAsync(source.Path, existing, "site", MaximumBytes, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("synthetic owner content", await File.ReadAllTextAsync(marker, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal([existing], Directory.GetFileSystemEntries(parent.Path));
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("site")]
    [InlineData("state")]
    [InlineData("audit")]
    [InlineData("schema")]
    [InlineData("schema-lookalike")]
    public async Task WrongIdentityOrDamagedImageNeverCreatesRestoreDestinationAsync(string defect)
    {
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        if (!await RequireSupportedProfileAsync(source.Path, parent.Path).ConfigureAwait(true)) return;
        await InitializeAsync(source.Path).ConfigureAwait(true);
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var created = await RegistrySnapshot.CreateAsync(source.Path, snapshot, "site", MaximumBytes, CancellationToken.None).ConfigureAwait(true);
        if (defect is "state" or "audit" or "schema" or "schema-lookalike") await DamageAsync(snapshot, defect).ConfigureAwait(true);
        var digest = string.Equals(defect, "digest", StringComparison.Ordinal) ? new string('A', 64) : await DigestAsync(snapshot).ConfigureAwait(true);
        var destination = Path.Combine(parent.Path, "refused");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await RegistrySnapshot.RestoreAsync(snapshot, destination, string.Equals(defect, "site", StringComparison.Ordinal) ? "other-site" : "site", MaximumBytes, digest, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.False(Directory.Exists(destination));
        Assert.Equal([snapshot], Directory.GetDirectories(parent.Path));
        if (defect is "digest" or "site") Assert.Equal(created.DatabaseSha256, await DigestAsync(snapshot).ConfigureAwait(true));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ005", Justification = "Assert.Single enforces exactly one restored policy/journal/status row; First would weaken the recovery guard.")]
    private static T Only<T>(IReadOnlyList<T> entries) => Assert.Single(entries);

    private static async Task VerifyJournalAndPolicyAsync(SqliteRegistryRepository repository, DnsMutationEntry mutation, RegistryTimeProvider clock)
    {
        var policy = await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(policy);
        Assert.Equal(1, policy.Revision);
        Assert.Equal("{\"site\":{\"algorithm\":1}}", policy.CanonicalJson);
        var journal = await repository.ReadDnsJournalAsync(Scope, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, journal.Revision);
        Assert.Equal(mutation, Only(journal.Entries));
        var issuer = Only(await new SqliteAcmeCertificateStatusPersistence(repository, "site.test", Issuer).ReadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("failed", issuer.LastResult);
        Assert.Equal(clock.GetUtcNow().AddSeconds(30), issuer.NextAttemptNotBeforeUtc);
    }

    private static async Task<bool> RequireSupportedProfileAsync(string source, string parent)
    {
        if (OperatingSystem.IsLinux()) return true;
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await RegistrySnapshot.CreateAsync(source, Path.Combine(parent, "unsupported"), "site", MaximumBytes, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        return false;
    }

    private static async Task InitializeAsync(string directory)
    {
        var repository = await SqliteRegistryRepository.OpenAsync(directory, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        await repository.CommitAsync(0, new RegistryState(1, RegistryState.Empty.Grants, RegistryState.Empty.Instances, []),
            new RegistryAudit(DateTimeOffset.UnixEpoch, "fixture", "administrator", "site"), CancellationToken.None).ConfigureAwait(true);
    }

    private static async Task<DnsMutationEntry> InitializeAuthorityAsync(string directory, RegistryTestFixture fixture, InstanceIntent intent)
    {
        var repository = await SqliteRegistryRepository.OpenAsync(directory, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        using var controller = new RegistryCoordinator(repository, fixture.Availability, fixture.Clock);
        await controller.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        await controller.EnrollAsync(fixture.Grant, "administrator", CancellationToken.None).ConfigureAwait(true);
        await controller.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        Assert.True(fixture.Availability.SetReadiness(intent.Identity, ready: true, TimeSpan.FromSeconds(120)));
        Assert.True(fixture.Availability.SetPublication(intent.Identity,
            new DestinationPublication(controller.State.Revision, 1, DnsVerified: true, CertificateVerified: true, fixture.Clock.GetUtcNow().AddMinutes(1))
            { Address = new PublishedServiceAddress("svc.site.test", "/") }));
        Assert.True(fixture.Availability.IsEligible(intent.Identity));
        var policy = new PolicyRevision(1, "{\"site\":{\"algorithm\":1}}", fixture.Clock.GetUtcNow(), "administrator", "control");
        Assert.True(await repository.TryCommitPolicyAsync(0, controller.State.Revision, policy, CancellationToken.None).ConfigureAwait(true));
        var mutation = new DnsMutationEntry(Guid.NewGuid(), "svc.site.test", 1, 300, Convert.ToBase64String(new byte[] { 192, 0, 2, 7 }),
            Remove: false, Guid.Empty, ExpectedZoneRevision: 9, "prepared", Revision: 0, Serial: 0, "", "/v2/operations/test");
        await repository.CommitDnsJournalAsync(Scope, 0, new DnsJournalState(1, [mutation]), CancellationToken.None).ConfigureAwait(true);
        var now = fixture.Clock.GetUtcNow();
        var status = new AcmeCertificateLifecycleStatus("site", Enabled: true, ["site.test", "*.site.test"], Active: false, "none",
            NotBeforeUtc: null, NotAfterUtc: null, now, now, LastSucceededAtUtc: null, now, now.AddSeconds(30), "failed", "synthetic issuance failure");
        await new SqliteAcmeCertificateStatusPersistence(repository, "site.test", Issuer).UpsertAsync(status, CancellationToken.None).ConfigureAwait(true);
        return mutation;
    }

    private static async Task PinReadAsync(SqliteConnection observer, System.Data.Common.DbTransaction transaction)
    {
        var command = observer.CreateCommand();
        await using var lifetime = command.ConfigureAwait(true);
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT revision FROM registry_state WHERE id=1";
        Assert.Equal(1L, await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(true));
    }

    private static async Task DamageAsync(string directory, string defect)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(directory, "registry.sqlite"), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        await using var lifetime = connection.ConfigureAwait(true);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(true);
        var command = connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(true);
        if (string.Equals(defect, "state", StringComparison.Ordinal))
            command.CommandText = "UPDATE registry_state SET state=X'7B7D' WHERE id=1";
        else if (string.Equals(defect, "audit", StringComparison.Ordinal))
            command.CommandText = "DELETE FROM registry_audit WHERE revision=1";
        else if (string.Equals(defect, "schema", StringComparison.Ordinal))
            command.CommandText = "CREATE TABLE foreign_authority(id INTEGER PRIMARY KEY)";
        else if (string.Equals(defect, "schema-lookalike", StringComparison.Ordinal))
            command.CommandText = "CREATE TABLE sqliteXforeign(id INTEGER PRIMARY KEY)";
        else throw new InvalidDataException("Unknown fixture defect.");
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [SupportedOSPlatform("linux")]
    private static async Task VerifyStandaloneAsync(string directory, string expectedDigest, bool allowLocks = false)
    {
        Assert.Equal(expectedDigest, await DigestAsync(directory).ConfigureAwait(true));
        Assert.False(File.Exists(Path.Combine(directory, "registry.sqlite-wal")));
        Assert.False(File.Exists(Path.Combine(directory, "registry.sqlite-shm")));
        if (!allowLocks) Assert.Equal([Path.Combine(directory, "registry.sqlite")], Directory.GetFileSystemEntries(directory));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, "registry.sqlite")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
    }

    private static async Task<string> DigestAsync(string directory)
    {
        var file = PrivateOwnerFile.OpenRead(Path.Combine(directory, "registry.sqlite"));
        await using var lifetime = file.ConfigureAwait(true);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, CancellationToken.None).ConfigureAwait(true));
    }

    private static SqliteConnection ReadOnlyConnection(string directory) =>
        new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "registry.sqlite"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
}
