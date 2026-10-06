namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public interface IProxyCircuitBreakerMetricsSink
{
    void CircuitOpened();
    void CircuitHalfOpened();
    void CircuitClosed();
    void CircuitRejected();
}
