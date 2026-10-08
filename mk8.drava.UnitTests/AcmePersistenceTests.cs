using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AcmePersistenceTests
{
    private static readonly Uri DirectoryUri = new("https://ca.example/directory");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task FailedAttemptBackoffAndHistorySurviveARealRepositoryRestartAsync()
    {
        using var directory = new RegistryStateDirectory(); var clock = new RegistryTimeProvider();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            using var first = new DevelopmentAsyncAcmeLifecycle(Persistence(repository), clock) { IncludeWildcard = true, FailActivation = true, RetryAfter = TimeSpan.FromSeconds(30) };
            await first.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.Equal(1, first.Issued); Assert.Equal("failed", first.Status.Get("site")!.LastResult);
        }
        var restored = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var restoredLifetime = restored.ConfigureAwait(true);
        using var second = new DevelopmentAsyncAcmeLifecycle(Persistence(restored), clock) { IncludeWildcard = true, FailActivation = true, RetryAfter = TimeSpan.FromSeconds(30) };
        await second.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, second.Issued); Assert.Equal(clock.GetUtcNow(), second.Status.Get("site")!.LastFailedAtUtc);
        clock.Advance(TimeSpan.FromSeconds(31));
        await second.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, second.Issued); Assert.Equal(clock.GetUtcNow().AddSeconds(30), second.Status.Get("site")!.NextAttemptNotBeforeUtc);
    }

    [Fact]
    public async Task InterruptedAttemptWasDurableBeforeMaterialWriteAndPreventsImmediateRestartIssuanceAsync()
    {
        using var directory = new RegistryStateDirectory(); var clock = new RegistryTimeProvider();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var persistence = Persistence(repository);
        using var first = new DevelopmentAsyncAcmeLifecycle(persistence, clock) { IncludeWildcard = true, BlockWriter = true, RetryAfter = TimeSpan.FromSeconds(30) };
        using var cancellation = new CancellationTokenSource();
        var check = Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first.Manager.CheckRenewalsAsync(cancellation.Token).ConfigureAwait(false));
        try
        {
            await first.WriterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            var status = RequireOne(await persistence.ReadAsync(CancellationToken.None).ConfigureAwait(true));
            Assert.Equal("attempting", status.LastResult); Assert.Equal(clock.GetUtcNow().AddSeconds(30), status.NextAttemptNotBeforeUtc);
        }
        finally { await cancellation.CancelAsync().ConfigureAwait(true); await check.ConfigureAwait(true); }
        Assert.True(first.WriterExited.Task.IsCompletedSuccessfully);
        using var restarted = new DevelopmentAsyncAcmeLifecycle(Persistence(repository), clock) { IncludeWildcard = true, RetryAfter = TimeSpan.FromSeconds(30) };
        await restarted.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, restarted.Issued); Assert.Equal("attempting", restarted.Status.Get("site")!.LastResult);
    }

    [Fact]
    public async Task LostWriterFenceCannotCommitAttemptOrContactIssuerAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        using var fixture = new DevelopmentAsyncAcmeLifecycle(Persistence(repository)) { IncludeWildcard = true };
        await fixture.Status.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        await MutateAsync(directory.Path, Mutation.Fence, null, null).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Manager.CheckRenewalsAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(0, fixture.Issued); Assert.Null(fixture.Status.Get("site"));
    }

    [Fact]
    public async Task FreshAcceptedCertificateKeepsDurableSuccessHistoryWithoutIssuingAgainAsync()
    {
        using var directory = new RegistryStateDirectory(); var clock = new RegistryTimeProvider();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var persistence = Persistence(repository); var now = clock.GetUtcNow();
        var active = new AcmeRenewalActiveCertificate(now.AddDays(-1), now.AddDays(89));
        var succeeded = new AcmeCertificateLifecycleStatus("site", true, ["site.example", "*.site.example"], true, "acme", active.NotBeforeUtc, active.NotAfterUtc,
            now.AddDays(59), now.AddDays(-1), now.AddDays(-1), null, now.AddDays(59), "succeeded", null);
        await persistence.UpsertAsync(succeeded, CancellationToken.None).ConfigureAwait(true);
        using var fixture = new DevelopmentAsyncAcmeLifecycle(persistence, clock) { IncludeWildcard = true, Previous = active, LifetimeAwareRenewal = true };
        await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, fixture.Issued); Assert.Equal(now.AddDays(-1), fixture.Status.Get("site")!.LastSucceededAtUtc);
        Assert.Equal("not-due", RequireOne(await persistence.ReadAsync(CancellationToken.None).ConfigureAwait(true)).LastResult);
    }

    [Fact]
    public async Task ChangedOwnerDirectoryCannotAdoptOrOverwriteHistoryAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var original = Persistence(repository); var status = Failed(DateTimeOffset.UtcNow);
        await original.UpsertAsync(status, CancellationToken.None).ConfigureAwait(true);
        var changed = new SqliteAcmeCertificateStatusPersistence(repository, "site.example", new Uri("https://other.example/directory"));
        await Assert.ThrowsAsync<InvalidDataException>(() => changed.ReadAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => changed.UpsertAsync(status, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(status.LastAttemptAtUtc, RequireOne(await original.ReadAsync(CancellationToken.None).ConfigureAwait(true)).LastAttemptAtUtc);
    }

    [Fact]
    public async Task ForeignCertificateScopeIsRejectedBeforeDurableWriteAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var foreign = new SqliteAcmeCertificateStatusPersistence(repository, "other.example", DirectoryUri);
        await Assert.ThrowsAsync<InvalidDataException>(() => foreign.UpsertAsync(Failed(DateTimeOffset.UtcNow), CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Empty(await Persistence(repository).ReadAsync(CancellationToken.None).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("schema")]
    [InlineData("status")]
    public async Task CorruptOrIncompleteHistoryCannotSupplyRetryAdmissionAsync(string corruption)
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var persistence = Persistence(repository); var status = Failed(DateTimeOffset.UtcNow);
        await persistence.UpsertAsync(status, CancellationToken.None).ConfigureAwait(true);
        var json = JsonSerializer.Serialize(status, Json);
        json = corruption switch
        {
            "duplicate" => json.Insert(1, "\"certificateId\":\"foreign\","),
            "missing" => "{}",
            "status" => json.Replace("\"failed\"", "\"succeeded\"", StringComparison.Ordinal),
            _ => json,
        };
        var bytes = Encoding.UTF8.GetBytes(json);
        var digest = string.Equals(corruption, "digest", StringComparison.Ordinal) ? new byte[32] : SHA256.HashData(bytes);
        var mutation = string.Equals(corruption, "schema", StringComparison.Ordinal) ? Mutation.Schema : Mutation.State;
        await MutateAsync(directory.Path, mutation, bytes, digest).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => persistence.ReadAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    private static SqliteAcmeCertificateStatusPersistence Persistence(SqliteRegistryRepository repository) => new(repository, "site.example", DirectoryUri);
    private static AcmeCertificateLifecycleStatus Failed(DateTimeOffset now) => new("site", true, ["site.example", "*.site.example"], false, "none",
        null, null, now, now, null, now, now.AddSeconds(30), "failed", "Owned development failure.");

    private static AcmeCertificateLifecycleStatus RequireOne(IReadOnlyList<AcmeCertificateLifecycleStatus> statuses)
    {
        Assert.Collection(statuses, static status => Assert.NotNull(status));
        return statuses[0];
    }
    private enum Mutation { Fence, Schema, State }

    private static async Task MutateAsync(string directory, Mutation mutation, byte[]? state, byte[]? digest)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "registry.sqlite"), Pooling = false }.ToString());
        await using var connectionLifetime = connection.ConfigureAwait(false);
        await connection.OpenAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();
        switch (mutation)
        {
            case Mutation.Fence: command.CommandText = "UPDATE registry_state SET fence=fence+1 WHERE id=1"; break;
            case Mutation.Schema: command.CommandText = "UPDATE acme_lifecycle SET schema_version=2 WHERE id=1"; break;
            case Mutation.State: command.CommandText = "UPDATE acme_lifecycle SET state=$state,digest=$digest WHERE id=1"; break;
            default: throw new InvalidOperationException("Unknown owned test mutation.");
        }
        if (state is not null) command.Parameters.AddWithValue("$state", state);
        if (digest is not null) command.Parameters.AddWithValue("$digest", digest);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
