using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed class ProxyRouteDiagnosticsRuntimeRoute : IProxyRouteDiagnosticsRoute
{
    public ProxyRouteDiagnosticsRuntimeRoute(RuntimeRoute runtimeRoute)
    {
        ArgumentNullException.ThrowIfNull(runtimeRoute);
        SiteName = runtimeRoute.SiteName;
        Name = runtimeRoute.Name;
        Host = runtimeRoute.Host;
        PathPrefix = runtimeRoute.PathPrefix;
        Action = ProxyRouteActionKindMapper.FromRuntimeActionText(runtimeRoute.Action);
        MaintenanceEnabled = runtimeRoute.Maintenance.Enabled;
        Maintenance = new ProxyRouteDiagnosticsMaintenancePolicy(runtimeRoute.Maintenance.Enabled, runtimeRoute.Maintenance.RetryAfterSeconds, runtimeRoute.Maintenance.ContentType, runtimeRoute.Maintenance.Body);
        HttpsRedirect = new ProxyRouteDiagnosticsHttpsRedirectPolicy(runtimeRoute.HttpsRedirect.Enabled, runtimeRoute.HttpsRedirect.StatusCode, runtimeRoute.HttpsRedirect.HttpsPort);
        CanonicalHost = new ProxyRouteDiagnosticsCanonicalHostPolicy(runtimeRoute.CanonicalHost.Enabled, runtimeRoute.CanonicalHost.TargetHost, runtimeRoute.CanonicalHost.StatusCode);
        Redirect = new ProxyRouteDiagnosticsRedirectPolicy(runtimeRoute.Redirect.StatusCode, runtimeRoute.Redirect.TargetUrl, runtimeRoute.Redirect.TargetPath, runtimeRoute.Redirect.PreserveQuery);
        StaticResponse = new ProxyRouteDiagnosticsStaticResponse(runtimeRoute.StaticResponse.StatusCode, runtimeRoute.StaticResponse.ContentType, runtimeRoute.StaticResponse.Body);
        PathRewrite = new ProxyRouteDiagnosticsPathRewrite(runtimeRoute.PathRewrite.StripPrefix, runtimeRoute.PathRewrite.ReplacePrefix, runtimeRoute.PathRewrite.Replacement);
        MaxRequestBodyBytes = runtimeRoute.ResolvedOptions.MaxRequestBodyBytes;
        Upstreams = RouteDiagnosticsList.Copy(runtimeRoute.Upstreams.Select(ToUpstream));
        CacheEnabled = runtimeRoute.Cache.Enabled;
        CacheMethods = RouteDiagnosticsList.Copy(runtimeRoute.Cache.Methods);
        RetryEnabled = runtimeRoute.Retry.Enabled;
        RetryMethods = RouteDiagnosticsList.Copy(runtimeRoute.Retry.RetryMethods);
    }

    public string SiteName { get; }
    public string Name { get; }
    public string Host { get; }
    public string PathPrefix { get; }
    public string Action { get; }
    public bool MaintenanceEnabled { get; }
    public ProxyRouteDiagnosticsMaintenancePolicy Maintenance { get; }
    public ProxyRouteDiagnosticsHttpsRedirectPolicy HttpsRedirect { get; }
    public ProxyRouteDiagnosticsCanonicalHostPolicy CanonicalHost { get; }
    public ProxyRouteDiagnosticsRedirectPolicy Redirect { get; }
    public ProxyRouteDiagnosticsStaticResponse StaticResponse { get; }
    public ProxyRouteDiagnosticsPathRewrite PathRewrite { get; }
    public long MaxRequestBodyBytes { get; }
    public IReadOnlyList<IProxyRouteDiagnosticsUpstream> Upstreams { get; }
    public bool CacheEnabled { get; }
    public IReadOnlyList<string> CacheMethods { get; }
    public bool RetryEnabled { get; }
    public IReadOnlyList<string> RetryMethods { get; }

    private static ProxyRouteDiagnosticsRuntimeUpstream ToUpstream(RuntimeUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new ProxyRouteDiagnosticsRuntimeUpstream(upstream);
    }
}
