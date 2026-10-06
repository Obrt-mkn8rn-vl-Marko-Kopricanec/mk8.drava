using System.Collections.ObjectModel;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyStatusConfigurationSummary
{
    public ProxyStatusConfigurationSummary(int Version, DateTimeOffset LoadedAtUtc, int ListenerCount, int RouteCount)
    {
        ProxyStatusFacts.RequireNonNegative(Version, nameof(Version));
        ProxyStatusFacts.RequireNonNegative(ListenerCount, nameof(ListenerCount));
        ProxyStatusFacts.RequireNonNegative(RouteCount, nameof(RouteCount));
        this.Version = Version;
        this.LoadedAtUtc = LoadedAtUtc;
        this.ListenerCount = ListenerCount;
        this.RouteCount = RouteCount;
    }

    public int Version { get; }
    public DateTimeOffset LoadedAtUtc { get; }
    public int ListenerCount { get; }
    public int RouteCount { get; }
}
