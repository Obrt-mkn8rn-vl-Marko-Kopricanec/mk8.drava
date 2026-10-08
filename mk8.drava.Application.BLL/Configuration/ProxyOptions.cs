namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";
    public IList<ListenerOptions> Listeners { get; init; } = [];
    public IList<ProxyRouteOptions> Routes { get; init; } = [];
}
