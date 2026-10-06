using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PolicyReconciliationTests
{
    [Fact]
    public async Task ControlAuthorityWinsOverFileWatchingAndRollbackCreatesANewAcceptedRevisionAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var runtime = new PolicyRuntime(repository, directory.Path);
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        await runtime.StartAsync().ConfigureAwait(true);
        await runtime.WaitAsync(static view => view.AppliedRevision == 1).ConfigureAwait(true);
        Assert.True(await runtime.Reconciler.UpdatePolicyAsync(1, "{\"site\":{\"algorithm\":\"least-active\"}}", importFile: false, CancellationToken.None).ConfigureAwait(true));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "noconf.json"), "{\"site\":{\"algorithm\":1}}").ConfigureAwait(true);
        await RegisterAsync(runtime).ConfigureAwait(true);
        var changed = await runtime.WaitAsync(view => view.Compiled?.DesiredRevision == runtime.Registry.State.Revision && view.Compiled.Services.ContainsKey("svc")).ConfigureAwait(true);
        Assert.NotNull(changed.Compiled);
        Assert.Equal(2, changed.AppliedRevision);
        Assert.Equal("control", changed.Accepted?.Source);
        Assert.Equal(BalancingAlgorithm.LeastActive, changed.Compiled.Services["svc"].Route.Balancing?.Algorithm);
        Assert.False(await runtime.Reconciler.UpdatePolicyAsync(1, "{}", importFile: false, CancellationToken.None).ConfigureAwait(true));
        Assert.True(await runtime.Reconciler.RollbackPolicyAsync(2, 1, CancellationToken.None).ConfigureAwait(true));
        var rolledBack = await runtime.WaitAsync(static view => view.AppliedRevision == 3).ConfigureAwait(true);
        Assert.Equal("control", rolledBack.Accepted?.Source);
        Assert.NotNull(rolledBack.Compiled);
        Assert.Equal(BalancingAlgorithm.PowerOfTwoChoices, rolledBack.Compiled.Services["svc"].Route.Balancing?.Algorithm);
        Assert.Collection(rolledBack.History, item => Assert.Equal(3, item.Revision), item => Assert.Equal(2, item.Revision), item => Assert.Equal(1, item.Revision));
        Assert.True(await runtime.Reconciler.UpdatePolicyAsync(3, null, importFile: true, CancellationToken.None).ConfigureAwait(true));
        var imported = await runtime.WaitAsync(static view => view.AppliedRevision == 4).ConfigureAwait(true);
        Assert.Equal("file", imported.Accepted?.Source);
        Assert.NotNull(imported.Compiled);
        Assert.Equal(BalancingAlgorithm.WeightedRoundRobin, imported.Compiled.Services["svc"].Route.Balancing?.Algorithm);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"site\":{\"affinityHeader\":\"X-Tenant\"}}")]
    public async Task BadWatchedFileAfterRestartRetainsDurablePolicyWithoutRestoringLeasesAsync(string rejectedJson)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "noconf.json");
        await File.WriteAllTextAsync(path, "{\"site\":{\"algorithm\":\"least-active\"}}").ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            var runtime = new PolicyRuntime(repository, directory.Path);
            await using var runtimeLifetime = runtime.ConfigureAwait(true);
            await runtime.StartAsync().ConfigureAwait(true);
            await runtime.WaitAsync(static view => view.AppliedRevision == 1).ConfigureAwait(true);
            await RegisterAsync(runtime).ConfigureAwait(true);
        }
        await File.WriteAllTextAsync(path, rejectedJson).ConfigureAwait(true);
        var restarted = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = restarted.ConfigureAwait(true);
        var restored = new PolicyRuntime(restarted, directory.Path);
        await using var restoredLifetime = restored.ConfigureAwait(true);
        await restored.StartAsync().ConfigureAwait(true);
        var view = await restored.WaitAsync(static view => view.AppliedRevision == 1 && view.Failure.Length > 0).ConfigureAwait(true);
        Assert.NotNull(view.Compiled);
        Assert.Equal(BalancingAlgorithm.LeastActive, view.Compiled.Services["svc"].Route.Balancing?.Algorithm);
        Assert.Collection(view.History, static revision => Assert.Equal(1, revision.Revision));
        Assert.Collection(restored.Registry.State.Instances.Values, intent =>
        {
            Assert.False(restored.Availability.IsEligible(intent.Identity));
            Assert.False(restored.Availability.Status(intent.Identity).LeaseValid);
        });
        await Task.Delay(2200).ConfigureAwait(true);
        var stable = await restored.Reconciler.ReadPolicyViewAsync(includeHistory: false, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(view.RuntimeVersion, stable.RuntimeVersion);
    }

    [Fact]
    public async Task InvalidCandidateCannotAdvanceDurableHistoryOrReplaceAcceptedPolicyAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var runtime = new PolicyRuntime(repository, directory.Path);
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        await runtime.StartAsync().ConfigureAwait(true);
        await runtime.WaitAsync(static view => view.AppliedRevision == 1).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await runtime.Reconciler.UpdatePolicyAsync(1, "{\"site\":{\"affinityHeader\":\"X-Tenant\"}}", importFile: false, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        var view = await runtime.Reconciler.ReadPolicyViewAsync(includeHistory: true, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, view.AppliedRevision);
        Assert.Equal(1, view.Accepted?.Revision);
        Assert.Collection(view.History, static revision => Assert.Equal(1, revision.Revision));
    }

    private static async Task<InstanceIntent> RegisterAsync(PolicyRuntime runtime)
    {
        var grant = new NodeGrant("node", "owner", RegistryTestFixture.Fingerprint, "svc", ["127.0.0.1"], 1024, 65535, DateTimeOffset.UtcNow.AddDays(1), revoked: false);
        await runtime.Registry.EnrollAsync(grant, "administrator", CancellationToken.None).ConfigureAwait(false);
        var intent = RegistryTestFixture.Intent();
        await runtime.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(false);
        return intent;
    }

    [Fact]
    public async Task LostPolicyWriterFenceClosesExistingLeasesAndPreventsSubsequentRegistrationAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var runtime = new PolicyRuntime(repository, directory.Path);
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        await runtime.StartAsync().ConfigureAwait(true);
        await runtime.WaitAsync(static view => view.AppliedRevision == 1).ConfigureAwait(true);
        var intent = await RegisterAsync(runtime).ConfigureAwait(true);
        runtime.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(30));
        runtime.Availability.SetPublication(intent.Identity, new DestinationPublication(runtime.Registry.State.Revision, 1, true, true, DateTimeOffset.UtcNow.AddSeconds(30)));
        Assert.True(runtime.Availability.IsEligible(intent.Identity));
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory.Path, "registry.sqlite"), Pooling = false }.ToString());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await connection.OpenAsync().ConfigureAwait(true);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE registry_state SET fence=fence+1 WHERE id=1";
        await command.ExecuteNonQueryAsync().ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await runtime.Reconciler.UpdatePolicyAsync(1, "{}", importFile: false, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.False(runtime.Registry.StorageHealthy);
        Assert.False(runtime.Availability.IsEligible(intent.Identity));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.Registry.RenewAsync(RegistryTestFixture.Fingerprint, intent.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }
}
