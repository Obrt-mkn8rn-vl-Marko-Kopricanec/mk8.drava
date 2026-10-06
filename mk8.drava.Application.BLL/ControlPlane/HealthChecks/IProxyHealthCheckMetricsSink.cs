namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public interface IProxyHealthCheckMetricsSink
{
    void HealthCheckAttempted();
    void HealthCheckSucceeded();
    void HealthCheckFailed();
}
