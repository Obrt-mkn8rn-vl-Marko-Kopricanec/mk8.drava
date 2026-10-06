using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyRouteSummarySource
{
    private string _siteName = string.Empty;
    public ProxyRouteSummarySource(string SiteName, bool IsProxyRoute, bool CacheEnabled, bool HasHttp3Upstream)
    {
        this.SiteName = SiteName;
        this.IsProxyRoute = IsProxyRoute;
        this.CacheEnabled = CacheEnabled;
        this.HasHttp3Upstream = HasHttp3Upstream;
    }

    public string SiteName
    {
        get => _siteName;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _siteName = value;
        }
    }

    public bool IsProxyRoute { get; init; }
    public bool CacheEnabled { get; init; }
    public bool HasHttp3Upstream { get; init; }
}
