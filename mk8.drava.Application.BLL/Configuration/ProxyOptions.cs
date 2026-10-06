namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";
    public System.Collections.ObjectModel.Collection<ListenerOptions> Listeners { get; init; } = [];
    public System.Collections.ObjectModel.Collection<ProxyRouteOptions> Routes { get; init; } = [];
}
