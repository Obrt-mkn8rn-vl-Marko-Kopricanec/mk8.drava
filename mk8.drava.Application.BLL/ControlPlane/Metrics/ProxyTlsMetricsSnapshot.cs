namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyTlsMetricsSnapshot(long HandshakeAttempts, long HandshakeSuccesses, long HandshakeFailures, long HandshakeTimeouts, long NoCertificateForSniFailures, long ActiveHandshakes, long HandshakeAdmissionRejections)
{
    public long HandshakeAttempts { get; } = MetricsList.RequireCounter(HandshakeAttempts, nameof(HandshakeAttempts));
    public long HandshakeSuccesses { get; } = MetricsList.RequireCounter(HandshakeSuccesses, nameof(HandshakeSuccesses));
    public long HandshakeFailures { get; } = MetricsList.RequireCounter(HandshakeFailures, nameof(HandshakeFailures));
    public long HandshakeTimeouts { get; } = MetricsList.RequireCounter(HandshakeTimeouts, nameof(HandshakeTimeouts));
    public long NoCertificateForSniFailures { get; } = MetricsList.RequireCounter(NoCertificateForSniFailures, nameof(NoCertificateForSniFailures));
    public long ActiveHandshakes { get; } = MetricsList.RequireCounter(ActiveHandshakes, nameof(ActiveHandshakes));
    public long HandshakeAdmissionRejections { get; } = MetricsList.RequireCounter(HandshakeAdmissionRejections, nameof(HandshakeAdmissionRejections));
}
