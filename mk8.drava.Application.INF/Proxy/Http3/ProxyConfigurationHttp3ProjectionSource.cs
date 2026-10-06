using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.INF.Proxy.Http3;
public sealed class ProxyConfigurationHttp3ProjectionSource : IProxyConfigurationHttp3ProjectionSource
{
    private readonly IRuntimeHttp3PlatformSupportSource _platformSupportSource;
    public ProxyConfigurationHttp3ProjectionSource(IRuntimeHttp3PlatformSupportSource platformSupportSource)
    {
        _platformSupportSource = platformSupportSource;
    }

    public RuntimeHttp3SupportProjection Project(Http3SupportConfigurationSource source)
    {
        return Http3RuntimeSupport.ProjectConfiguration(source, _platformSupportSource.Read());
    }
}
