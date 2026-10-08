using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class NoConfStartupTests
{
    [Fact]
    public async Task InvalidInitialPolicyDoesNotStartBackgroundLoopsAsync()
    {
        using var directory = new RegistryStateDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "noconf.json"), "{\"mode\":\"invalid\"}").ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var runtime = new PolicyRuntime(repository, directory.Path);
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync()).ConfigureAwait(true);
        Assert.Null(runtime.Reconciler.Compiled); Assert.Null(runtime.Reconciler.ExecuteTask);
    }

    [Fact]
    public async Task CanceledInitialPolicyReadIsJoinedBeforeStartupReturnsAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        using var blocked = new DevelopmentBlockedPolicyRepository(repository);
        var runtime = new PolicyRuntime(repository, directory.Path, blocked);
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        await runtime.Registry.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource();
        var started = runtime.Reconciler.StartAsync(cancellation.Token);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        await cancellation.CancelAsync().ConfigureAwait(true);
        var canceled = false;
        try { await started.ConfigureAwait(true); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.True(canceled); Assert.True(runtime.Registry.StorageHealthy);
        Assert.Null(runtime.Reconciler.Compiled); Assert.Null(runtime.Reconciler.ExecuteTask);
    }

    [Fact]
    public async Task StartupWaitsForRestoredRouteCompilationWithoutRestoringLiveEligibilityAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        using (var previous = new RegistryCoordinator(repository, new DestinationAvailabilityStore(TimeProvider.System), TimeProvider.System))
        {
            await previous.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            await previous.EnrollAsync(new NodeGrant("node", "owner", RegistryTestFixture.Fingerprint, "svc", ["127.0.0.1"], 1024, 65535, DateTimeOffset.UtcNow.AddDays(1), revoked: false), "administrator", CancellationToken.None).ConfigureAwait(true);
            await previous.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        }
        using var blocked = new DevelopmentBlockedPolicyRepository(repository);
        var runtime = new PolicyRuntime(repository, directory.Path, blocked);
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        await runtime.Registry.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        var started = runtime.Reconciler.StartAsync(CancellationToken.None);
        try
        {
            await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            Assert.False(started.IsCompleted); Assert.Null(runtime.Reconciler.Compiled);
            blocked.Release(); await started.ConfigureAwait(true);
            var compiled = runtime.Reconciler.Compiled;
            Assert.NotNull(compiled); Assert.Equal("svc.site.example", compiled.Services["svc"].Route.Host);
            Assert.False(runtime.Availability.Status(intent.Identity).LeaseValid); Assert.False(runtime.Availability.IsEligible(intent.Identity));
        }
        finally { blocked.Release(); await started.ConfigureAwait(true); }
    }
}
