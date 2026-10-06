namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public interface IUpstreamHealthCheckClient
{
    ValueTask<HealthCheckSample> CheckAsync(UpstreamHealthCheckTarget target, CancellationToken cancellationToken);
}
