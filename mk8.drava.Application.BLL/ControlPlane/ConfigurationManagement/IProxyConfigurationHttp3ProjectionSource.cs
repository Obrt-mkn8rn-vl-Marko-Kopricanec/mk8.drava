using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationHttp3ProjectionSource
{
    RuntimeHttp3SupportProjection Project(Http3SupportConfigurationSource source);
}
