using System.Net;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.NoConf;

public sealed class NoConfSnapshotCompiler(IProxyEndpointAddressPolicy addresses, IProxyUrlSyntaxPolicy urls, NoConfIngressGuard? ingress = null)
{
    public CompiledNoConfSnapshot Compile(RegistryState state, ProxyConfigurationSnapshot baseline, NoConfPolicy policy, string domain, string localNodeId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(domain);
        RegistryNames.RequireLabel(localNodeId);
        RequireDomain(domain);
        // Validate the policy envelope even when the registry is empty.
        var policyIndex = new NoConfPolicyIndex(policy);
        var groups = GroupInstances(state);
        ValidatePolicies(policyIndex, baseline, domain, localNodeId, groups.Keys);
        var routes = new List<RuntimeRoute>();
        if (!string.Equals(policy.Mode, "auto", StringComparison.Ordinal)) routes.AddRange(baseline.Routes);
        var services = new Dictionary<string, CompiledNoConfService>(StringComparer.Ordinal);
        if (!string.Equals(policy.Mode, "manual", StringComparison.Ordinal))
        {
            foreach (var pool in groups)
            {
                var resolved = NoConfPolicyResolver.Resolve(policyIndex, pool.Key, domain);
                var route = CompileService(resolved, pool.Value, baseline, localNodeId);
                routes.Add(route);
                services.Add(pool.Key, new CompiledNoConfService(pool.Key, route, resolved.Provenance));
            }
        }
        return new CompiledNoConfSnapshot(state.Revision, baseline.WithListenersAndRoutes(baseline.Listeners, routes), services);
    }

    private void ValidatePolicies(NoConfPolicyIndex index, ProxyConfigurationSnapshot baseline, string domain, string localNodeId, IEnumerable<string> registeredServices)
    {
        _ = Validate("validation");
        var services = new HashSet<string>(index.Services.Keys, StringComparer.Ordinal);
        services.UnionWith(registeredServices);
        var claims = new List<RuntimeRoute>();
        foreach (var service in services) claims.Add(Validate(service));
        if (!string.Equals(index.Policy.Mode, "manual", StringComparison.Ordinal))
            NoConfRouteOwnership.Validate(string.Equals(index.Policy.Mode, "auto", StringComparison.Ordinal) ? [] : baseline.Routes, claims);
        RuntimeRoute Validate(string serviceId)
        {
            var resolved = NoConfPolicyResolver.Resolve(index, serviceId, domain);
            var identity = new RegisteredUpstreamIdentity(localNodeId, "validation", serviceId, "v1", new string('2', 32), new string('1', 32));
            var intent = new InstanceIntent(identity, "validation", "127.0.0.1", 1, "http1", "http", "/", "validation", 1, draining: false);
            return CompileService(resolved, [intent], baseline, localNodeId, validateIngress: false);
        }
    }

    private RuntimeRoute CompileService(ResolvedNoConfInputs resolved, List<InstanceIntent> intents, ProxyConfigurationSnapshot baseline, string localNodeId, bool validateIngress = true)
    {
        var input = resolved.Route;
        if (input.Overrides.MaxRequestBodyBytes > baseline.Limits.MaxRequestBodyBytes)
            throw new InvalidDataException("Service body override exceeds the site ceiling.");
        for (var index = 0; index < intents.Count; index++)
        {
            var intent = intents[index];
            if (validateIngress) ingress?.Validate(intent);
            input.Upstreams.Add(new UpstreamOptions { Name = intent.Identity.InstanceId, Scheme = intent.Scheme, Protocol = intent.Protocol,
                Address = intent.Address, Port = intent.Port, Weight = intent.Weight, UpstreamTls = resolved.Tls, CircuitBreaker = resolved.Circuit });
        }
        var options = new ProxyOptions { Listeners = [new ListenerOptions { Name = "validation", Address = "127.0.0.1", Port = 8080 }], Routes = [input] };
        var errors = ProxyOptionsValidationRules.Validate(options, addresses, urls);
        if (errors.Count > 0) throw new InvalidDataException("Invalid service policy: " + string.Join(" ", errors));
        var mapped = ProxyConfigurationRuntimeMapper.ToRuntimeRoutes([input], new ProxyOperationalOptions())[0];
        var upstreams = new List<RuntimeUpstream>();
        for (var index = 0; index < intents.Count; index++)
        {
            var intent = intents[index];
            // A remote loopback literal belongs to its node. It is never connected to as controller-local.
            if (intent.Draining || (intent.Relay is null && !string.Equals(intent.Identity.NodeId, localNodeId, StringComparison.Ordinal) && IPAddress.IsLoopback(IPAddress.Parse(intent.Address)))) continue;
            var upstream = mapped.Upstreams[index];
            upstreams.Add(new RuntimeUpstream(upstream.RouteName, upstream.Name, upstream.Scheme, upstream.Protocol, upstream.Address, upstream.Port,
                upstream.Weight, upstream.Tls, upstream.CircuitBreaker, intent.Identity));
        }
        return new RuntimeRoute(mapped.Name, mapped.Host, mapped.PathPrefix, mapped.Action, "registered", mapped.HealthCheck, upstreams,
            mapped.HttpsRedirect, mapped.CanonicalHost, mapped.HeaderPolicy, mapped.PathRewrite, mapped.Redirect, mapped.StaticResponse, mapped.Maintenance,
            mapped.Cache, ResolveLimits(input.Overrides, baseline), mapped.SiteName, mapped.Retry, resolved.Balancing);
    }

    private static RuntimeRouteResolvedOptions ResolveLimits(ProxyRouteOverrideOptions overrides, ProxyConfigurationSnapshot baseline) => new(
        overrides.MaxRequestBodyBytes ?? baseline.Limits.MaxRequestBodyBytes,
        overrides.ClientRequestHeadTimeoutMs is { } requestTimeout ? TimeSpan.FromMilliseconds(requestTimeout) : baseline.Timeouts.ClientRequestHeadTimeout,
        overrides.UpstreamResponseHeadTimeoutMs is { } responseTimeout ? TimeSpan.FromMilliseconds(responseTimeout) : baseline.Timeouts.UpstreamResponseHeadTimeout,
        overrides.AccessLogEnabled ?? baseline.Observability.AccessLogEnabled);

    private static SortedDictionary<string, List<InstanceIntent>> GroupInstances(RegistryState state)
    {
        var groups = new SortedDictionary<string, List<InstanceIntent>>(StringComparer.Ordinal);
        foreach (var intent in state.Instances.Values)
        {
            if (!groups.TryGetValue(intent.Identity.ServiceId, out var group)) groups.Add(intent.Identity.ServiceId, group = []);
            group.Add(intent);
        }
        foreach (var group in groups.Values)
            group.Sort(static (left, right) => string.Compare(left.Identity.InstanceId, right.Identity.InstanceId, StringComparison.Ordinal));
        return groups;
    }

    private static void RequireDomain(string domain)
    {
        if (domain.Length is < 3 or > 189 || !domain.Contains('.', StringComparison.Ordinal)) throw new InvalidDataException("Site domain leaves no room for a full service label.");
        foreach (var label in domain.Split('.')) RegistryNames.RequireLabel(label);
    }
}
