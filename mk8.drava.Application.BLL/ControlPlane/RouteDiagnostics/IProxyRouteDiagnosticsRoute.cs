namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public interface IProxyRouteDiagnosticsRoute
{
    string SiteName { get; }

    string Name { get; }

    string Host { get; }

    string PathPrefix { get; }

    string Action { get; }

    bool MaintenanceEnabled { get; }

    ProxyRouteDiagnosticsMaintenancePolicy Maintenance { get; }

    ProxyRouteDiagnosticsHttpsRedirectPolicy HttpsRedirect { get; }

    ProxyRouteDiagnosticsCanonicalHostPolicy CanonicalHost { get; }

    ProxyRouteDiagnosticsRedirectPolicy Redirect { get; }

    ProxyRouteDiagnosticsStaticResponse StaticResponse { get; }

    ProxyRouteDiagnosticsPathRewrite PathRewrite { get; }

    long MaxRequestBodyBytes { get; }

    IReadOnlyList<IProxyRouteDiagnosticsUpstream> Upstreams { get; }

    bool CacheEnabled { get; }

    IReadOnlyList<string> CacheMethods { get; }

    bool RetryEnabled { get; }

    IReadOnlyList<string> RetryMethods { get; }
}
