using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class RouteMatcherTests
{
    public static void MatchesWildcardRoute()
    {
        var matcher = new SingleUpstreamRouteMatcher();
        var snapshot = Snapshot(new ProxyOptions { Routes = [new ProxyRouteOptions { Name = "default", Host = "*", PathPrefix = "/", Upstreams = [new UpstreamOptions { Name = "local", Address = "127.0.0.1", Port = 5000 }] }] });
        var request = new Http1RequestHead("GET", "/anything", "/anything", "HTTP/1.1", "example.test", Http1RequestFraming.None, []);
        var match = Match(matcher, snapshot.Routes, request);
        AssertEx.NotNull(match);
        var route = snapshot.Routes[match!.RouteIndex];
        AssertEx.Equal("default", route.Name);
        AssertEx.Equal("local", route.Upstreams[0].Name);
    }

    public static void MatchesHostWithoutRequestPort()
    {
        var matcher = new SingleUpstreamRouteMatcher();
        var snapshot = Snapshot(new ProxyOptions { Routes = [new ProxyRouteOptions { Name = "example", Host = "example.test", PathPrefix = "/api", Upstreams = [new UpstreamOptions { Name = "api", Address = "127.0.0.1", Port = 5001 }] }] });
        var request = new Http1RequestHead("GET", "/api/status", "/api/status", "HTTP/1.1", "example.test:8080", Http1RequestFraming.None, []);
        var match = Match(matcher, snapshot.Routes, request);
        AssertEx.NotNull(match);
        var route = snapshot.Routes[match!.RouteIndex];
        AssertEx.Equal("example", route.Name);
        AssertEx.Equal("api", route.Upstreams[0].Name);
    }

    public static void ExactHostRouteBeatsWildcardFallbackWhenBothCouldMatch()
    {
        var matcher = new SingleUpstreamRouteMatcher();
        var snapshot = Snapshot(new ProxyOptions { Routes = [Route("exact", "example.test", "/", "exact-upstream", 5001), Route("wildcard", "*", "/", "wildcard-upstream", 5002)] });
        var request = Request("GET", "/resource", "example.test");
        var match = AssertEx.NotNull(Match(matcher, snapshot.Routes, request));
        var route = snapshot.Routes[match.RouteIndex];
        AssertEx.Equal("exact", route.Name);
        AssertEx.Equal("exact-upstream", route.Upstreams[0].Name);
    }

    public static void PortSpecificHostRouteBeatsHostFallbackWhenAuthorityIncludesPort()
    {
        var matcher = new SingleUpstreamRouteMatcher();
        var snapshot = Snapshot(new ProxyOptions { Routes = [Route("port-specific", "example.test:8443", "/", "port-upstream", 5001), Route("host-only", "example.test", "/", "host-upstream", 5002)] });
        var request = Request("GET", "/resource", "example.test:8443");
        var match = AssertEx.NotNull(Match(matcher, snapshot.Routes, request));
        var route = snapshot.Routes[match.RouteIndex];
        AssertEx.Equal("port-specific", route.Name);
        AssertEx.Equal("port-upstream", route.Upstreams[0].Name);
    }

    public static void SpecificRoutePathBeatsCatchAllFallbackWhenBothCouldMatch()
    {
        var matcher = new SingleUpstreamRouteMatcher();
        var snapshot = Snapshot(new ProxyOptions { Routes = [Route("api", "example.test", "/api", "api-upstream", 5001), Route("default", "example.test", "/", "default-upstream", 5002)] });
        var request = Request("GET", "/api/users", "example.test");
        var match = AssertEx.NotNull(Match(matcher, snapshot.Routes, request));
        var route = snapshot.Routes[match.RouteIndex];
        AssertEx.Equal("api", route.Name);
        AssertEx.Equal("api-upstream", route.Upstreams[0].Name);
    }

    public static void MatchesRuntimeRoutesWithoutConfigurationSnapshot()
    {
        var matcher = new SingleUpstreamRouteMatcher();
        var routes = Snapshot(new ProxyOptions { Routes = [Route("api", "example.test", "/api", "api-upstream", 5001)] }).Routes;
        var request = Request("GET", "/api/users", "example.test");
        var candidates = ProxyRouteMatchRuntimeMapper.ToCandidates(routes.Select(static route => route));
        var match = AssertEx.NotNull(matcher.Match(candidates, ProxyRouteMatchRuntimeMapper.ToRequest(request)));
        var route = ProxyRouteMatchRuntimeMapper.SelectRoute(routes, match);
        AssertEx.Equal("api", route.Name);
        AssertEx.Equal("api-upstream", route.Upstreams[0].Name);
        AssertEx.False(candidates is RouteMatchCandidate[], "Route match candidates should not expose a mutable array.");
    }

    public static void RouteMatchRuntimeMapperRejectsNullInputs()
    {
        var routes = Snapshot(new ProxyOptions { Routes = [Route("api", "example.test", "/api", "api-upstream", 5001)] }).Routes;
        var request = Request("GET", "/api/users", "example.test");
        var match = new RouteMatch(0);
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteMatchRuntimeMapper.ToCandidates(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteMatchRuntimeMapper.ToCandidates([null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteMatchRuntimeMapper.ToRequest(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteMatchRuntimeMapper.SelectRoute(null!, match));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteMatchRuntimeMapper.SelectRoute(routes, null!));
        AssertEx.Throws<ArgumentException>(() => new RouteMatch(-1));
        AssertEx.Throws<ArgumentException>(() => new RouteMatchCandidate(null!, "/api"));
        AssertEx.Throws<ArgumentException>(() => new RouteMatchCandidate(" ", "/api"));
        AssertEx.Throws<ArgumentException>(() => new RouteMatchCandidate("example.test", null!));
        AssertEx.Throws<ArgumentException>(() => new RouteMatchCandidate("example.test", "api"));
        AssertEx.Throws<ArgumentException>(() => new RouteMatchRequest(null!, "/api"));
        AssertEx.Throws<ArgumentException>(() => new RouteMatchRequest(" ", "/api"));
        AssertEx.Throws<ArgumentException>(() => new RouteMatchRequest("example.test", null!));
        AssertEx.Throws<ArgumentException>(() => new RouteMatchRequest("example.test", "api"));
        AssertEx.Equal("example.test", ProxyRouteMatchRuntimeMapper.ToRequest(request).Host);
        AssertEx.Equal("api", ProxyRouteMatchRuntimeMapper.SelectRoute(routes, match).Name);
    }

    private static RouteMatch? Match(SingleUpstreamRouteMatcher matcher, IReadOnlyList<RuntimeRoute> routes, Http1RequestHead request)
    {
        return matcher.Match(ProxyRouteMatchRuntimeMapper.ToCandidates(routes), ProxyRouteMatchRuntimeMapper.ToRequest(request));
    }

    private static ProxyRouteOptions Route(string name, string host, string pathPrefix, string upstreamName, int upstreamPort)
    {
        return new ProxyRouteOptions
        {
            Name = name,
            Host = host,
            PathPrefix = pathPrefix,
            Upstreams = [new UpstreamOptions
            {
                Name = upstreamName,
                Address = "127.0.0.1",
                Port = upstreamPort
            }

            ]
        };
    }

    private static Http1RequestHead Request(string method, string target, string host)
    {
        var path = target.Split('?', 2)[0];
        return new Http1RequestHead(method, target, path, "HTTP/1.1", host, Http1RequestFraming.None, []);
    }

    private static ProxyConfigurationSnapshot Snapshot(ProxyOptions options)
    {
        var operationalOptions = new ProxyOperationalOptions();
        return ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(options, operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UnixEpoch, "test", [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("test", "test/config", "test/config/sites", "test/logs", "test/certs", "test/state", "test/config/proxy.json"), [], [], []));
    }
}
