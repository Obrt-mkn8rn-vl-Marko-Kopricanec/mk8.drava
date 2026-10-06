using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RegistryRevocationTests
{
    [Fact]
    public async Task InstanceRevocationStopsNewReservationsPreservesActiveWorkAndAllowsOnlyANewBootAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        var upstream = await fixture.ReadyAsync(intent).ConfigureAwait(true);
        var route = RegistryTestFixture.Route([upstream]);
        using var existing = fixture.Selector.Reserve(route, null);
        Assert.NotNull(existing);
        await fixture.Registry.RevokeInstanceAsync(intent.Identity, "administrator", CancellationToken.None).ConfigureAwait(true);
        Assert.Null(fixture.Selector.Reserve(route, null));
        Assert.Equal(1, fixture.Availability.ActiveRequests(upstream));
        Assert.Collection(fixture.Registry.State.Instances.Values, static retired => Assert.True(retired.Draining));
        Assert.Contains(intent.Identity.Partition, fixture.Registry.State.Tombstones, StringComparer.Ordinal);
        var retiredPool = NoConfCompilerTests.Compiler().Compile(fixture.Registry.State, NoConfCompilerTests.Baseline(), new NoConfPolicy(), "site.example", "node");
        Assert.Empty(retiredPool.Services["svc"].Route.Upstreams);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        var replacement = RegistryTestFixture.Intent(instance: intent.Identity.InstanceId);
        await fixture.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, replacement, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        Assert.False(fixture.Availability.IsEligible(replacement.Identity));
        existing.Dispose();
        Assert.Equal(0, fixture.Availability.ActiveRequests(upstream));
    }

    [Fact]
    public async Task NodeRevocationTombstonesSurviveRestartAndReenrollmentAsync()
    {
        using var directory = new RegistryStateDirectory();
        using var fixture = new RegistryTestFixture();
        var intent = RegistryTestFixture.Intent();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            using var registry = new RegistryCoordinator(repository, fixture.Availability, fixture.Clock);
            await registry.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            await registry.EnrollAsync(fixture.Grant, "administrator", CancellationToken.None).ConfigureAwait(true);
            await registry.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
            await registry.RevokeNodeAsync("node", "administrator", CancellationToken.None).ConfigureAwait(true);
        }
        var restarted = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = restarted.ConfigureAwait(true);
        var availability = new DestinationAvailabilityStore(fixture.Clock);
        using var restored = new RegistryCoordinator(restarted, availability, fixture.Clock);
        await restored.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.True(restored.State.Grants["node"].Revoked);
        Assert.Collection(restored.State.Instances.Values, static retired => Assert.True(retired.Draining));
        Assert.Contains(intent.Identity.Partition, restored.State.Tombstones, StringComparer.Ordinal);
        await restored.EnrollAsync(fixture.Grant, "administrator", CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await restored.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.False(availability.IsEligible(intent.Identity));
    }

    [Fact]
    public async Task AuthorityClosureCannotBeUndoneThroughTheAvailabilityStoreAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(intent).ConfigureAwait(true);
        fixture.Registry.FailStorage();
        Assert.False(fixture.Registry.StorageHealthy);
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
        Assert.Throws<InvalidOperationException>(() => fixture.Availability.Renew(intent, fixture.Grant.NotAfterUtc, TimeSpan.FromSeconds(90)));
        Assert.False(fixture.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(30)));
        Assert.False(fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(1, 1, true, true, fixture.Clock.GetUtcNow().AddSeconds(30))));
    }

    [Fact]
    public async Task RevocationDoesNotReleaseDurableServiceOwnershipAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        await fixture.Registry.RevokeInstanceAsync(intent.Identity, "administrator", CancellationToken.None).ConfigureAwait(true);
        var fingerprint = new string('B', 64);
        var otherGrant = new NodeGrant("other-node", "other-owner", fingerprint, "svc", ["127.0.0.1"], 1024, 65535, fixture.Clock.GetUtcNow().AddDays(1), revoked: false);
        await fixture.Registry.EnrollAsync(otherGrant, "administrator", CancellationToken.None).ConfigureAwait(true);
        var other = new InstanceIntent(new RegisteredUpstreamIdentity("other-node", "other-owner", "svc", "v1", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")),
            "deployment", "127.0.0.1", 12345, "http1", "http", "/ready", "local", 1, draining: false);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Registry.RegisterAsync(fingerprint, other, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }
}
