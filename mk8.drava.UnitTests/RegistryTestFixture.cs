using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

namespace Mk8.Drava.UnitTests;

internal sealed class RegistryTestFixture : IDisposable
{
    public const string Fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    public RegistryTestFixture()
    {
        Availability = new DestinationAvailabilityStore(Clock);
        Registry = new RegistryCoordinator(Repository, Availability, Clock);
        var metrics = new ProxyMetrics();
        var circuits = new CircuitBreakerStore(metrics, Clock);
        Selector = new PolicyUpstreamSelector(Availability, new UpstreamHealthStore(metrics, new EmptyPruner(), circuits), circuits, metrics);
    }
    public RegistryTimeProvider Clock { get; } = new();
    public MemoryRegistryRepository Repository { get; } = new();
    public DestinationAvailabilityStore Availability { get; }
    public RegistryCoordinator Registry { get; }
    public PolicyUpstreamSelector Selector { get; }
    public NodeGrant Grant => new("node", "owner", Fingerprint, "svc", ["127.0.0.1", "192.0.2.10"], 1024, 65535, Clock.GetUtcNow().AddDays(1), revoked: false);
    public static InstanceIntent Intent(string? instance = null, string? boot = null, string address = "127.0.0.1", string zone = "local", int weight = 1) =>
        new(new RegisteredUpstreamIdentity("node", "owner", "svc", "v1", instance ?? Guid.NewGuid().ToString("N"), boot ?? Guid.NewGuid().ToString("N")),
            "deployment", address, 12345, "http1", "http", "/ready", zone, weight, draining: false);
    public async ValueTask InitializeAsync()
    {
        await Registry.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        await Registry.EnrollAsync(Grant, "administrator", CancellationToken.None).ConfigureAwait(false);
    }
    public async ValueTask<RuntimeUpstream> ReadyAsync(InstanceIntent intent)
    {
        await Registry.RegisterAsync(Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(false);
        Publish(intent);
        return Upstream(intent);
    }
    public void Publish(InstanceIntent intent)
    {
        Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(120));
        Availability.SetPublication(intent.Identity, new DestinationPublication(Registry.State.Revision, 1, true, true, Clock.GetUtcNow().AddHours(1)));
    }
    public static RuntimeUpstream Upstream(InstanceIntent intent) => new(intent.Identity.ServiceId, intent.Identity.InstanceId, intent.Scheme, intent.Protocol,
        intent.Address, intent.Port, intent.Weight, RuntimeUpstreamTlsOptions.Default, RuntimeCircuitBreakerPolicy.Disabled, intent.Identity);
    public static UpstreamSelectionRoute Route(IReadOnlyList<RuntimeUpstream> upstreams, UpstreamBalancingPolicy? policy = null) =>
        new("svc", false, upstreams) { Policy = policy ?? UpstreamBalancingPolicy.Default };
    public void Dispose() => Registry.Dispose();
    private sealed class EmptyPruner : IUpstreamConnectionPruner
    {
        public void PruneIdleConnections(UpstreamTransportEndpoint endpoint) => ArgumentNullException.ThrowIfNull(endpoint);
    }
}
