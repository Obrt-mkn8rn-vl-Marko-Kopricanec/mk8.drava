using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.NoConf;

public static class NoConfPolicyResolver
{
    public static ResolvedNoConfInputs Resolve(NoConfPolicy policy, string serviceId, string domain)
    {
        return Resolve(new NoConfPolicyIndex(policy), serviceId, domain);
    }

    internal static ResolvedNoConfInputs Resolve(NoConfPolicyIndex index, string serviceId, string domain)
    {
        RegistryNames.RequireLabel(serviceId);
        var sources = new List<(string Name, NoConfPolicyPatch Patch)> { ("global", index.Policy.Global), ("site", index.Policy.Site) };
        if (index.Services.TryGetValue(serviceId, out var service))
        {
            sources.Add(("service-profile", service.Profile));
            sources.Add(("route", service.Route));
        }
        var provenance = new Dictionary<string, string>(StringComparer.Ordinal);
        T Choose<T>(string name, Func<NoConfPolicyPatch, T?> read, T fallback) where T : class
        {
            var value = fallback;
            provenance[name] = "default";
            for (var index = 0; index < sources.Count; index++)
                if (read(sources[index].Patch) is { } configured) { value = configured; provenance[name] = sources[index].Name; }
            return value;
        }
        T? ChooseOptional<T>(string name, Func<NoConfPolicyPatch, T?> read) where T : struct
        {
            T? value = null;
            provenance[name] = "default";
            for (var index = 0; index < sources.Count; index++)
                if (read(sources[index].Patch) is { } configured) { value = configured; provenance[name] = sources[index].Name; }
            return value;
        }
        T ChooseValue<T>(string name, Func<NoConfPolicyPatch, T?> read, T fallback) where T : struct => ChooseOptional(name, read) ?? fallback;
        var balancing = new UpstreamBalancingPolicy(ChooseValue("balancing", static patch => patch.Algorithm, BalancingAlgorithm.PowerOfTwoChoices),
            Choose("affinityHeader", static patch => patch.AffinityHeader, "") is { Length: > 0 } affinity ? affinity : null,
            Choose("preferredZone", static patch => patch.PreferredZone, "") is { Length: > 0 } zone ? zone : null,
            ChooseValue("requireLocalZone", static patch => patch.RequireLocalZone, false));
        var route = new ProxyRouteOptions
        {
            Name = "registered-" + serviceId, SiteName = "registered",
            Host = Choose("host", static patch => patch.Host, serviceId + "." + domain),
            PathPrefix = Choose("pathPrefix", static patch => patch.PathPrefix, "/"),
            Action = Choose("action", static patch => patch.Action, "proxy"),
            Redirect = Choose("redirect", static patch => patch.Redirect, new ProxyRedirectOptions()),
            StaticResponse = Choose("staticResponse", static patch => patch.StaticResponse, new ProxyStaticResponseOptions()),
            HeaderPolicy = Choose("headers", static patch => patch.Headers, new ProxyHeaderPolicyOptions()),
            PathRewrite = Choose("rewrite", static patch => patch.Rewrite, new ProxyPathRewriteOptions()),
            Cache = Choose("cache", static patch => patch.Cache, new ProxyCachePolicyOptions()),
            Retry = Choose("retry", static patch => patch.Retry, new ProxyRetryPolicyOptions()),
            Maintenance = Choose("maintenance", static patch => patch.Maintenance, new ProxyMaintenanceOptions()),
            HttpsRedirect = Choose("httpsRedirect", static patch => patch.HttpsRedirect, new ProxyHttpsRedirectOptions()),
            CanonicalHost = Choose("canonicalHost", static patch => patch.CanonicalHost, new ProxyCanonicalHostOptions()),
            Overrides = new ProxyRouteOverrideOptions
            {
                MaxRequestBodyBytes = ChooseOptional("limits.maxRequestBodyBytes", static patch => patch.Limits?.MaxRequestBodyBytes),
                ClientRequestHeadTimeoutMs = ChooseOptional("limits.clientRequestHeadTimeoutMs", static patch => patch.Limits?.ClientRequestHeadTimeoutMs),
                UpstreamResponseHeadTimeoutMs = ChooseOptional("limits.upstreamResponseHeadTimeoutMs", static patch => patch.Limits?.UpstreamResponseHeadTimeoutMs),
                ClientRequestBodyIdleTimeoutMs = ChooseOptional("limits.clientRequestBodyIdleTimeoutMs", static patch => patch.Limits?.ClientRequestBodyIdleTimeoutMs),
                UpstreamConnectTimeoutMs = ChooseOptional("limits.upstreamConnectTimeoutMs", static patch => patch.Limits?.UpstreamConnectTimeoutMs),
                UpstreamResponseBodyIdleTimeoutMs = ChooseOptional("limits.upstreamResponseBodyIdleTimeoutMs", static patch => patch.Limits?.UpstreamResponseBodyIdleTimeoutMs),
                DownstreamWriteTimeoutMs = ChooseOptional("limits.downstreamWriteTimeoutMs", static patch => patch.Limits?.DownstreamWriteTimeoutMs),
                AccessLogEnabled = ChooseOptional("limits.accessLogEnabled", static patch => patch.Limits?.AccessLogEnabled),
            },
        };
        ValidateRoute(route, domain);
        return new ResolvedNoConfInputs(balancing, route, Choose("upstreamTls", static patch => patch.UpstreamTls, new UpstreamTlsOptions()),
            Choose("circuitBreaker", static patch => patch.CircuitBreaker, new ProxyCircuitBreakerOptions()), provenance);
    }

    private static void ValidateRoute(ProxyRouteOptions route, string domain)
    {
        if (!string.Equals(route.Action, "proxy", StringComparison.OrdinalIgnoreCase) && !string.Equals(route.Action, "redirect", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(route.Action, "staticResponse", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Automatic route action must be proxy, redirect or staticResponse.");
        _ = new PublishedServiceAddress(route.Host, route.PathPrefix);
        if (string.Equals(route.Host, "register." + domain, StringComparison.Ordinal))
            throw new InvalidDataException("The site registration hostname is reserved.");
    }
}
