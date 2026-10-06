namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public static class UpstreamHealthStateText
{
    public static string FromState(UpstreamHealthState state)
    {
        return state switch
        {
            UpstreamHealthState.Unknown => nameof(UpstreamHealthState.Unknown),
            UpstreamHealthState.Healthy => nameof(UpstreamHealthState.Healthy),
            UpstreamHealthState.Unhealthy => nameof(UpstreamHealthState.Unhealthy),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown upstream health state.")};
    }
}
