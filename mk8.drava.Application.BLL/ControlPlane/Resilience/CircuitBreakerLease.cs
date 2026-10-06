namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public sealed class CircuitBreakerLease : IDisposable
{
    private readonly Action<CircuitBreakerLease> _release;
    private int _completed;
    internal CircuitBreakerLease(CircuitBreakerStatusSource source, bool enabled, bool halfOpenProbe, Action<CircuitBreakerLease> release)
    {
        Source = source;
        Enabled = enabled;
        HalfOpenProbe = halfOpenProbe;
        _release = release;
    }

    public CircuitBreakerStatusSource Source { get; }
    public bool Enabled { get; }
    public bool HalfOpenProbe { get; }

    internal bool TryComplete()
    {
        return Interlocked.Exchange(ref _completed, 1) == 0;
    }

    public void Dispose()
    {
        if (TryComplete())
        {
            _release(this);
        }
    }
}
