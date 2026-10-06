namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public interface IUpstreamHealthCheckEventSink
{
    void Checked(string routeName, string upstreamName, string endpoint, string result, UpstreamHealthState state);
}
