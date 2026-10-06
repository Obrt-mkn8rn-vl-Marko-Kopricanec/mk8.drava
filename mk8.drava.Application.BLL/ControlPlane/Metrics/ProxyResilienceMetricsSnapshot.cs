namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyResilienceMetricsSnapshot
{
    public ProxyResilienceMetricsSnapshot(long RetryAttempts, long RetryExhausted, IEnumerable<ProxyRetrySkippedSnapshot> RetrySkipped, long CircuitOpened, long CircuitHalfOpened, long CircuitClosed, long CircuitRejections, long NoAvailableUpstreamFailures)
    {
        this.RetryAttempts = RetryAttempts;
        this.RetryExhausted = RetryExhausted;
        this.RetrySkipped = MetricsList.Copy(RetrySkipped);
        this.CircuitOpened = CircuitOpened;
        this.CircuitHalfOpened = CircuitHalfOpened;
        this.CircuitClosed = CircuitClosed;
        this.CircuitRejections = CircuitRejections;
        this.NoAvailableUpstreamFailures = NoAvailableUpstreamFailures;
    }

    public long RetryAttempts { get; }
    public long RetryExhausted { get; }
    public IReadOnlyList<ProxyRetrySkippedSnapshot> RetrySkipped { get; }
    public long CircuitOpened { get; }
    public long CircuitHalfOpened { get; }
    public long CircuitClosed { get; }
    public long CircuitRejections { get; }
    public long NoAvailableUpstreamFailures { get; }
}
