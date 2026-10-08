using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AcmeCompletionBackoffTests
{
    [Theory]
    [InlineData("issuer", 30)]
    [InlineData("writer", 30)]
    [InlineData("activation", 30)]
    [InlineData("issuer", 86400)]
    [InlineData("writer", 86400)]
    [InlineData("activation", 86400)]
    public async Task SlowFailureRetainsAFullRetryDelayAcrossRepositoryRestartAsync(string stage, int retrySeconds)
    {
        using var directory = new RegistryStateDirectory(); var clock = new RegistryTimeProvider();
        var started = clock.GetUtcNow();
        Action fail = () => { clock.Advance(TimeSpan.FromMinutes(2)); throw new IOException("Owned slow failure."); };
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            using var fixture = new DevelopmentAsyncAcmeLifecycle(Persistence(repository), clock)
            {
                IncludeWildcard = true, RetryAfter = TimeSpan.FromSeconds(retrySeconds),
                BeforeIssue = string.Equals(stage, "issuer", StringComparison.Ordinal) ? fail : null,
                BeforeWrite = string.Equals(stage, "writer", StringComparison.Ordinal) ? fail : null,
                BeforeActivation = string.Equals(stage, "activation", StringComparison.Ordinal) ? fail : null,
            };
            await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
            var status = fixture.Status.Get("site"); Assert.NotNull(status);
            Assert.Equal("failed", status.LastResult); Assert.Equal(started, status.LastAttemptAtUtc);
            Assert.Equal(clock.GetUtcNow().AddSeconds(retrySeconds), status.NextAttemptNotBeforeUtc);
            Assert.Equal(clock.GetUtcNow(), status.LastFailedAtUtc);
            await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.Equal(1, fixture.Issued);
        }
        var restored = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var restoredLifetime = restored.ConfigureAwait(true);
        using var restarted = new DevelopmentAsyncAcmeLifecycle(Persistence(restored), clock) { IncludeWildcard = true, RetryAfter = TimeSpan.FromSeconds(retrySeconds) };
        clock.Advance(TimeSpan.FromSeconds(retrySeconds - 1));
        await restarted.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, restarted.Issued);
        clock.Advance(TimeSpan.FromSeconds(1));
        await restarted.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, restarted.Issued);
    }

    [Fact]
    public async Task SuccessfulHistoryRecordsCompletionAfterAllOwnedPhasesAsync()
    {
        var clock = new RegistryTimeProvider(); var started = clock.GetUtcNow();
        Action complete = () => clock.Advance(TimeSpan.FromMinutes(2));
        using var fixture = new DevelopmentAsyncAcmeLifecycle(clock: clock) { BeforeIssue = complete, BeforeWrite = complete, BeforeActivation = complete };
        await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        var status = fixture.Status.Get("site"); Assert.NotNull(status);
        Assert.Equal("succeeded", status.LastResult); Assert.Equal(started, status.LastAttemptAtUtc);
        Assert.Equal(started.AddMinutes(6), status.LastSucceededAtUtc);
        Assert.Equal(1, fixture.Activated); Assert.Equal(1, fixture.Succeeded);
    }

    private static SqliteAcmeCertificateStatusPersistence Persistence(SqliteRegistryRepository repository) =>
        new(repository, "site.example", new Uri("https://ca.example/directory"));
}
