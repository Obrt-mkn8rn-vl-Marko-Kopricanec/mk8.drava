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

    [Theory]
    [InlineData("issuer")]
    [InlineData("writer")]
    [InlineData("activation")]
    public async Task SlowInterruptedAttemptReceivesAFullRetryDelayAfterRepositoryRecoveryAsync(string stage)
    {
        using var directory = new RegistryStateDirectory(); var clock = new RegistryTimeProvider();
        var started = clock.GetUtcNow();
        Action interrupt = () => { clock.Advance(TimeSpan.FromMinutes(2)); throw new OperationCanceledException("Owned interrupted renewal."); };
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            using var fixture = new DevelopmentAsyncAcmeLifecycle(Persistence(repository), clock)
            {
                IncludeWildcard = true, RetryAfter = TimeSpan.FromSeconds(30),
                BeforeIssue = string.Equals(stage, "issuer", StringComparison.Ordinal) ? interrupt : null,
                BeforeWrite = string.Equals(stage, "writer", StringComparison.Ordinal) ? interrupt : null,
                BeforeActivation = string.Equals(stage, "activation", StringComparison.Ordinal) ? interrupt : null,
            };
            await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Manager.CheckRenewalsAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
            var admission = fixture.Status.Get("site"); Assert.NotNull(admission);
            Assert.Equal("attempting", admission.LastResult); Assert.Equal(started, admission.LastAttemptAtUtc);
            Assert.True(admission.NextAttemptNotBeforeUtc < clock.GetUtcNow());
        }
        var recoveredAt = clock.GetUtcNow();
        var restored = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var restoredLifetime = restored.ConfigureAwait(true);
        using var restarted = new DevelopmentAsyncAcmeLifecycle(Persistence(restored), clock) { IncludeWildcard = true, RetryAfter = TimeSpan.FromSeconds(30) };
        await restarted.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, restarted.Issued);
        var status = restarted.Status.Get("site"); Assert.NotNull(status);
        Assert.Equal("failed", status.LastResult); Assert.Equal(started, status.LastAttemptAtUtc);
        Assert.Equal(recoveredAt, status.LastFailedAtUtc); Assert.Equal(recoveredAt.AddSeconds(30), status.NextAttemptNotBeforeUtc);
        clock.Advance(TimeSpan.FromSeconds(29));
        await restarted.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true); Assert.Equal(0, restarted.Issued);
        clock.Advance(TimeSpan.FromSeconds(1));
        await restarted.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true); Assert.Equal(1, restarted.Issued);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledOwnedOperationIsJoinedAndRecoveryRetainsAFullRetryDelayAsync(bool activation)
    {
        using var directory = new RegistryStateDirectory(); var clock = new RegistryTimeProvider();
        var started = clock.GetUtcNow();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            using var fixture = new DevelopmentAsyncAcmeLifecycle(Persistence(repository), clock)
                { IncludeWildcard = true, RetryAfter = TimeSpan.FromSeconds(30), BlockWriter = !activation, BlockActivation = activation };
            using var cancellation = new CancellationTokenSource();
            var operation = fixture.Manager.CheckRenewalsAsync(cancellation.Token).AsTask();
            try
            {
                await (activation ? fixture.ActivationEntered.Task : fixture.WriterEntered.Task).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                clock.Advance(TimeSpan.FromMinutes(2));
                await cancellation.CancelAsync().ConfigureAwait(true);
                var canceled = false;
                try { await operation.ConfigureAwait(true); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; }
                Assert.True(canceled);
                AssertCanceledPhaseHasEnded(fixture, activation);
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(true);
                try { await operation.ConfigureAwait(true); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }
        }
        var recoveredAt = clock.GetUtcNow();
        var restored = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var restoredLifetime = restored.ConfigureAwait(true);
        using var restarted = new DevelopmentAsyncAcmeLifecycle(Persistence(restored), clock) { IncludeWildcard = true, RetryAfter = TimeSpan.FromSeconds(30) };
        await restarted.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true); Assert.Equal(0, restarted.Issued);
        var status = restarted.Status.Get("site"); Assert.NotNull(status);
        Assert.Equal("failed", status.LastResult); Assert.Equal(started, status.LastAttemptAtUtc);
        Assert.Equal(recoveredAt, status.LastFailedAtUtc); Assert.Equal(recoveredAt.AddSeconds(30), status.NextAttemptNotBeforeUtc);
        clock.Advance(TimeSpan.FromSeconds(29));
        await restarted.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true); Assert.Equal(0, restarted.Issued);
        clock.Advance(TimeSpan.FromSeconds(1));
        await restarted.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true); Assert.Equal(1, restarted.Issued);
    }

    private static void AssertCanceledPhaseHasEnded(DevelopmentAsyncAcmeLifecycle fixture, bool activation)
    {
        Assert.True((activation ? fixture.ActivationExited.Task : fixture.WriterExited.Task).IsCompletedSuccessfully);
        Assert.Equal(0, fixture.Succeeded); Assert.Equal(0, fixture.Activated);
        var admission = fixture.Status.Get("site"); Assert.NotNull(admission); Assert.Equal("attempting", admission.LastResult);
    }

    [Fact]
    public async Task ConcurrentCheckDoesNotRecoverAnAttemptThatIsStillActivatingAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clock = new RegistryTimeProvider();
        using var fixture = new DevelopmentAsyncAcmeLifecycle(clock: clock) { BlockActivation = true, RetryAfter = TimeSpan.FromSeconds(30) };
        var first = fixture.Manager.CheckRenewalsAsync(timeout.Token).AsTask();
        Task? second = null;
        try
        {
            await fixture.ActivationEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(true);
            clock.Advance(TimeSpan.FromMinutes(2));
            second = fixture.Manager.CheckRenewalsAsync(timeout.Token).AsTask();
            Assert.True(second.IsCompleted, "A second check must not start or reinterpret an already owned live attempt.");
            await second.ConfigureAwait(true);
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Manager.CheckRenewalsAsync(canceled.Token).AsTask()).ConfigureAwait(true);
            Assert.Equal(1, fixture.Issued);
            var status = fixture.Status.Get("site"); Assert.NotNull(status); Assert.Equal("attempting", status.LastResult);
        }
        finally
        {
            fixture.ReleaseActivation();
            if (second is not null) fixture.ReleaseActivation();
            await Task.WhenAll(second is null ? [first] : new[] { first, second }).WaitAsync(timeout.Token).ConfigureAwait(true);
        }
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
