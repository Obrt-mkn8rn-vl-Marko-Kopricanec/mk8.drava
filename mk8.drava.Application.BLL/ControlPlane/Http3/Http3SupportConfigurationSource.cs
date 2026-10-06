using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public sealed record Http3SupportConfigurationSource
{
    public static Http3SupportConfigurationSource Empty { get; } = new([], false);

    public Http3SupportConfigurationSource(IEnumerable<Http3SupportListenerSource> Listeners, bool UpstreamHttp3Configured)
    {
        ArgumentNullException.ThrowIfNull(Listeners);
        this.Listeners = Http3List.Copy(Listeners);
        this.UpstreamHttp3Configured = UpstreamHttp3Configured;
    }

    public IReadOnlyList<Http3SupportListenerSource> Listeners { get; }
    public bool UpstreamHttp3Configured { get; }
}
