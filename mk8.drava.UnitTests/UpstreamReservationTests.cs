using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class UpstreamReservationTests
{
    [Fact]
    public async Task DefaultChoicesAvoidAReplicaWithAnActiveSlowRequestAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var first = await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var second = await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        using var slow = fixture.Selector.Reserve(RegistryTestFixture.Route([first]), null);
        Assert.NotNull(slow);
        var route = RegistryTestFixture.Route([first, second]);
        for (var index = 0; index < 500; index++)
        {
            using var request = fixture.Selector.Reserve(route, null);
            Assert.NotNull(request);
            Assert.Equal(second.Identity, request.Selection.Upstream.Identity);
        }
        Assert.Equal(1, fixture.Availability.ActiveRequests(first));
        Assert.Equal(0, fixture.Availability.ActiveRequests(second));
    }

    [Fact]
    public async Task ConcurrentSelectionReservesExactlyOneActiveSlotAndReleaseIsIdempotentAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var first = await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var second = await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var route = RegistryTestFixture.Route([first, second]);
        var reservations = new UpstreamReservation?[200];
        try
        {
            Parallel.For(0, reservations.Length, index => reservations[index] = fixture.Selector.Reserve(route, null));
            Assert.All(reservations, Assert.NotNull);
            Assert.Equal(200, fixture.Availability.ActiveRequests(first) + fixture.Availability.ActiveRequests(second));
            Assert.InRange(Math.Abs(fixture.Availability.ActiveRequests(first) - fixture.Availability.ActiveRequests(second)), 0, 1);
        }
        finally
        {
            foreach (var reservation in reservations) { reservation?.Dispose(); reservation?.Dispose(); }
        }
        Assert.Equal(0, fixture.Availability.ActiveRequests(first));
        Assert.Equal(0, fixture.Availability.ActiveRequests(second));
    }

    [Fact]
    public async Task ConfiguredRoundRobinHonorsWeightsAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var first = await fixture.ReadyAsync(RegistryTestFixture.Intent(weight: 3)).ConfigureAwait(true);
        var second = await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var route = RegistryTestFixture.Route([first, second], new UpstreamBalancingPolicy(BalancingAlgorithm.WeightedRoundRobin));
        var selectedFirst = 0;
        for (var index = 0; index < 400; index++)
        {
            using var request = fixture.Selector.Reserve(route, null);
            Assert.NotNull(request);
            if (string.Equals(request.Selection.Upstream.Identity, first.Identity, StringComparison.Ordinal)) selectedFirst++;
        }
        Assert.Equal(300, selectedFirst);
    }

    [Fact]
    public async Task StableHashIgnoresCandidateOrderAndRequiresItsDeclaredKeyAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var first = await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var second = await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var policy = new UpstreamBalancingPolicy(BalancingAlgorithm.StableHash, affinityHeader: "X-Tenant");
        var route = RegistryTestFixture.Route([first, second], policy);
        var reverse = RegistryTestFixture.Route([second, first], policy);
        using var selected = fixture.Selector.Reserve(route, "tenant-a");
        Assert.NotNull(selected);
        for (var index = 0; index < 100; index++)
        {
            using var repeated = fixture.Selector.Reserve(reverse, "tenant-a");
            Assert.NotNull(repeated);
            Assert.Equal(selected.Selection.Upstream.Identity, repeated.Selection.Upstream.Identity);
        }
        Assert.Throws<InvalidDataException>(() => fixture.Selector.Reserve(route, null));
    }

    [Fact]
    public async Task LocalityCanPreferFallbackOrRequireOnlyLocalInstancesAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var local = await fixture.ReadyAsync(RegistryTestFixture.Intent(zone: "local")).ConfigureAwait(true);
        var remote = await fixture.ReadyAsync(RegistryTestFixture.Intent(zone: "remote")).ConfigureAwait(true);
        using var preferred = fixture.Selector.Reserve(RegistryTestFixture.Route([remote, local], new UpstreamBalancingPolicy(BalancingAlgorithm.LeastActive, preferredZone: "local")), null);
        Assert.NotNull(preferred);
        Assert.Equal(local.Identity, preferred.Selection.Upstream.Identity);
        using var fallback = fixture.Selector.Reserve(RegistryTestFixture.Route([remote], new UpstreamBalancingPolicy(BalancingAlgorithm.LeastActive, preferredZone: "local")), null);
        Assert.NotNull(fallback);
        Assert.Null(fixture.Selector.Reserve(RegistryTestFixture.Route([remote], new UpstreamBalancingPolicy(BalancingAlgorithm.LeastActive, preferredZone: "local", requireLocalZone: true)), null));
    }

    [Fact]
    public async Task WeightedDefaultChoicesDoNotInvertWeightsOnIdleReplicasAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var first = await fixture.ReadyAsync(RegistryTestFixture.Intent(weight: 3)).ConfigureAwait(true);
        var second = await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var route = RegistryTestFixture.Route([first, second]);
        var selectedFirst = 0;
        for (var index = 0; index < 4000; index++)
        {
            using var request = fixture.Selector.Reserve(route, null);
            Assert.NotNull(request);
            if (string.Equals(request.Selection.Upstream.Identity, first.Identity, StringComparison.Ordinal)) selectedFirst++;
        }
        Assert.InRange(selectedFirst, 2600, 3400);
    }

    [Fact]
    public async Task PoolPartitionChangesAcrossBootEvenAtTheSameNetworkAddressAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var original = RegistryTestFixture.Intent();
        var first = await fixture.ReadyAsync(original).ConfigureAwait(true);
        var second = await fixture.ReadyAsync(RegistryTestFixture.Intent(instance: original.Identity.InstanceId)).ConfigureAwait(true);
        Assert.NotEqual(UpstreamTransportEndpointMapper.FromUpstream(first).PoolKey, UpstreamTransportEndpointMapper.FromUpstream(second).PoolKey, StringComparer.Ordinal);
        Assert.Null(fixture.Selector.Reserve(RegistryTestFixture.Route([first]), null));
        using var current = fixture.Selector.Reserve(RegistryTestFixture.Route([second]), null);
        Assert.NotNull(current);
    }
}
