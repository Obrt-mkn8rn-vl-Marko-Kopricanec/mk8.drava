using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed class ProxyStatusUpstreamHealthReader : IProxyStatusUpstreamHealthReader
{
    private readonly IProxyStatusConfigurationSource _configurationSource;
    private readonly IProxyStatusUpstreamHealthSource _upstreamHealthSource;
    public ProxyStatusUpstreamHealthReader(IProxyStatusConfigurationSource configurationSource, IProxyStatusUpstreamHealthSource upstreamHealthSource)
    {
        _configurationSource = configurationSource;
        _upstreamHealthSource = upstreamHealthSource;
    }

    public IReadOnlyList<ProxyUpstreamStatus> ReadUpstreams()
    {
        var configurationResult = _configurationSource.ReadConfiguration();
        var configuration = configurationResult is ProxyStatusConfigurationReadResult.AvailableResult available ? available.Configuration : null;
        return _upstreamHealthSource.ReadUpstreams(configuration is null ? [] : configuration.UpstreamHealthSources);
    }
}
