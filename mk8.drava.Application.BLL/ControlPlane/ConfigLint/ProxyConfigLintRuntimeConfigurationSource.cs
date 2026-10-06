using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ProxyConfigLintRuntimeConfigurationSource
{
    public ProxyConfigLintRuntimeConfigurationSource(IEnumerable<string> SourceFiles, IEnumerable<string> AdminUrls, bool AdminRequiresAuthentication, bool PublicMetricsEnabled, Http3SupportConfigurationSource Http3Support, IEnumerable<ProxyConfigLintRuntimeListenerSource> Listeners, IEnumerable<ProxyConfigLintRuntimeRouteSource> Routes)
    {
        ArgumentNullException.ThrowIfNull(SourceFiles);
        ArgumentNullException.ThrowIfNull(AdminUrls);
        ArgumentNullException.ThrowIfNull(Http3Support);
        ArgumentNullException.ThrowIfNull(Listeners);
        ArgumentNullException.ThrowIfNull(Routes);
        this.SourceFiles = ConfigLintList.Copy(SourceFiles);
        this.AdminUrls = ConfigLintList.Copy(AdminUrls);
        this.AdminRequiresAuthentication = AdminRequiresAuthentication;
        this.PublicMetricsEnabled = PublicMetricsEnabled;
        this.Http3Support = Http3Support;
        this.Listeners = ConfigLintList.Copy(Listeners.Select(RequireListenerSource));
        this.Routes = ConfigLintList.Copy(Routes.Select(RequireRouteSource));
    }

    public IReadOnlyList<string> SourceFiles { get; }
    public IReadOnlyList<string> AdminUrls { get; }
    public bool AdminRequiresAuthentication { get; }
    public bool PublicMetricsEnabled { get; }
    public Http3SupportConfigurationSource Http3Support { get; }
    public IReadOnlyList<ProxyConfigLintRuntimeListenerSource> Listeners { get; }
    public IReadOnlyList<ProxyConfigLintRuntimeRouteSource> Routes { get; }

    private static ProxyConfigLintRuntimeListenerSource RequireListenerSource(ProxyConfigLintRuntimeListenerSource listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return listener;
    }

    private static ProxyConfigLintRuntimeRouteSource RequireRouteSource(ProxyConfigLintRuntimeRouteSource route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route;
    }
}
