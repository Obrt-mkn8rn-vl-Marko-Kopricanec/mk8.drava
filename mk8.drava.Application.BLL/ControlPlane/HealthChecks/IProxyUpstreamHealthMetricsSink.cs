namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public interface IProxyUpstreamHealthMetricsSink
{
    void UpstreamHealthTransition();
    void UpstreamRequestFailed();
}
