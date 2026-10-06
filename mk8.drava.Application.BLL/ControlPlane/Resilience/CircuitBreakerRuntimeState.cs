namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public enum CircuitBreakerRuntimeState
{
    Disabled = 0,
    Closed,
    Open,
    HalfOpen
}
