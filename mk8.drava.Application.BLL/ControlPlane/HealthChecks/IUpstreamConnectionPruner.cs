using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public interface IUpstreamConnectionPruner
{
    void PruneIdleConnections(UpstreamTransportEndpoint endpoint);
}
