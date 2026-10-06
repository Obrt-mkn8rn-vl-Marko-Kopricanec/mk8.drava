namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyHttp3MetricsSnapshot
{
    public ProxyHttp3MetricsSnapshot(long AcceptedConnections, long ActiveConnections, long Requests, long ProxiedRequests, long GeneratedResponses, long ActiveStreams, long StreamResets, long StreamedResponses, long ActiveResponseStreams, long ResponseBytesSent, long RequestBodyBytesReceived, long ResponseStreamResets, long AltSvcEmitted, long AltSvcSuppressed, IEnumerable<ProxyHttp3RequestOutcomeSnapshot> RequestsByOutcome, IReadOnlyDictionary<string, long> RejectedRequests, IReadOnlyDictionary<string, long> ProtocolErrors, long QuicListenerStartSuccesses, long QuicListenerStartFailures, long ActiveQuicListeners)
    {
        this.AcceptedConnections = AcceptedConnections;
        this.ActiveConnections = ActiveConnections;
        this.Requests = Requests;
        this.ProxiedRequests = ProxiedRequests;
        this.GeneratedResponses = GeneratedResponses;
        this.ActiveStreams = ActiveStreams;
        this.StreamResets = StreamResets;
        this.StreamedResponses = StreamedResponses;
        this.ActiveResponseStreams = ActiveResponseStreams;
        this.ResponseBytesSent = ResponseBytesSent;
        this.RequestBodyBytesReceived = RequestBodyBytesReceived;
        this.ResponseStreamResets = ResponseStreamResets;
        this.AltSvcEmitted = AltSvcEmitted;
        this.AltSvcSuppressed = AltSvcSuppressed;
        this.RequestsByOutcome = MetricsList.Copy(RequestsByOutcome);
        this.RejectedRequests = MetricsList.CopyCounterDictionary(RejectedRequests, StringComparer.Ordinal);
        this.ProtocolErrors = MetricsList.CopyCounterDictionary(ProtocolErrors, StringComparer.Ordinal);
        this.QuicListenerStartSuccesses = QuicListenerStartSuccesses;
        this.QuicListenerStartFailures = QuicListenerStartFailures;
        this.ActiveQuicListeners = ActiveQuicListeners;
    }

    public long AcceptedConnections { get; }
    public long ActiveConnections { get; }
    public long Requests { get; }
    public long ProxiedRequests { get; }
    public long GeneratedResponses { get; }
    public long ActiveStreams { get; }
    public long StreamResets { get; }
    public long StreamedResponses { get; }
    public long ActiveResponseStreams { get; }
    public long ResponseBytesSent { get; }
    public long RequestBodyBytesReceived { get; }
    public long ResponseStreamResets { get; }
    public long AltSvcEmitted { get; }
    public long AltSvcSuppressed { get; }
    public IReadOnlyList<ProxyHttp3RequestOutcomeSnapshot> RequestsByOutcome { get; }
    public IReadOnlyDictionary<string, long> RejectedRequests { get; }
    public IReadOnlyDictionary<string, long> ProtocolErrors { get; }
    public long QuicListenerStartSuccesses { get; }
    public long QuicListenerStartFailures { get; }
    public long ActiveQuicListeners { get; }
}
