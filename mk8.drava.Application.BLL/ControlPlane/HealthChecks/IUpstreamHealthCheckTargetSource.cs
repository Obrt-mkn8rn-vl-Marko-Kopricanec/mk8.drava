namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public interface IUpstreamHealthCheckTargetSource
{
    IReadOnlyList<UpstreamHealthCheckTarget> ReadTargets();
}
