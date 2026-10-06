using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public sealed class ProxyRequestContext
{
    private readonly TimeProvider _timeProvider;
    private readonly long _startedTimestamp;
    public ProxyRequestContext(string requestId, string listenerName, string transport, string? clientEndpoint, int configVersion, TimeProvider timeProvider, string protocol = "http1")
    {
        _timeProvider = timeProvider;
        _startedTimestamp = timeProvider.GetTimestamp();
        RequestId = requestId;
        ListenerName = listenerName;
        Transport = transport;
        ClientEndpoint = clientEndpoint;
        ConfigVersion = configVersion;
        Protocol = protocol;
        StartedAtUtc = timeProvider.GetUtcNow();
    }

    public string RequestId { get; }
    public string? ExternalRequestId { get; private set; }
    public string ListenerName { get; }
    public string Transport { get; }
    public string Protocol { get; }
    public string? ClientEndpoint { get; private set; }
    public int ConfigVersion { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public string? Method { get; private set; }
    public string? Host { get; private set; }
    public string? Target { get; private set; }
    public string? RouteName { get; private set; }
    public string? SiteName { get; private set; }
    public string? RouteAction { get; private set; }
    public string? UpstreamName { get; private set; }
    public string? UpstreamEndpoint { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public bool ResponseStarted { get; private set; }
    public bool KeepClientConnectionOpen { get; private set; }
    public bool IsUpgrade { get; private set; }
    public bool TunnelEstablished { get; private set; }
    public string? TunnelCloseReason { get; private set; }
    public long TunnelBytesClientToUpstream { get; private set; }
    public long TunnelBytesUpstreamToClient { get; private set; }
    public ProxyFailureKind FailureKind { get; private set; } = ProxyFailureKind.None;
    public bool? AccessLogEnabled { get; private set; }
    public TimeSpan Elapsed => _timeProvider.GetElapsedTime(_startedTimestamp);

    public void SetRequest(string method, string host, string target, string? externalRequestId)
    {
        Method = method;
        Host = host;
        Target = target;
        ExternalRequestId = externalRequestId;
    }

    public void SetRoute(ProxyRequestRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        RouteName = route.Name;
        SiteName = route.SiteName;
        RouteAction = route.Action;
        AccessLogEnabled = route.AccessLogEnabled;
    }

    public void SetUpstream(ProxyRequestUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        UpstreamName = upstream.Name;
        UpstreamEndpoint = upstream.Endpoint;
    }

    public void SetClientEndpoint(string? clientEndpoint)
    {
        ClientEndpoint = clientEndpoint;
    }

    public void RecordGeneratedFailureResponse(ProxyGeneratedFailureResponse response, bool keepClientConnectionOpen)
    {
        ArgumentNullException.ThrowIfNull(response);
        ResponseStarted = true;
        ResponseStatusCode = response.StatusCode;
        FailureKind = response.FailureKind;
        KeepClientConnectionOpen = keepClientConnectionOpen;
    }

    public void RecordGeneratedRouteResponse(GeneratedRouteResponse response, bool keepClientConnectionOpen)
    {
        ArgumentNullException.ThrowIfNull(response);
        ResponseStarted = true;
        ResponseStatusCode = response.StatusCode;
        KeepClientConnectionOpen = keepClientConnectionOpen;
    }

    public void RecordCachedResponse(CachedProxyResponse response, bool keepClientConnectionOpen)
    {
        ArgumentNullException.ThrowIfNull(response);
        ResponseStarted = true;
        ResponseStatusCode = response.StatusCode;
        KeepClientConnectionOpen = keepClientConnectionOpen;
        SetRouteAction("cache");
    }

    public void RecordForwardingResult(ForwardingResult result, bool keepClientConnectionOpen)
    {
        ArgumentNullException.ThrowIfNull(result);
        ResponseStarted = result.ResponseStarted;
        ResponseStatusCode = result.ResponseStatusCode;
        FailureKind = result.FailureKind;
        KeepClientConnectionOpen = keepClientConnectionOpen;
    }

    public void RecordTunnelCompletion(ForwardingResult.TunnelCompletedResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        TunnelEstablished = result.ResponseStatusCode == 101;
        TunnelCloseReason = result.Tunnel.CloseReason;
        TunnelBytesClientToUpstream = result.Tunnel.BytesClientToUpstream;
        TunnelBytesUpstreamToClient = result.Tunnel.BytesUpstreamToClient;
    }

    public void RecordClientDisconnect()
    {
        FailureKind = ProxyFailureKind.ClientDisconnected;
    }

    public void RecordUpgradeRequest()
    {
        IsUpgrade = true;
    }

    public void RecordClientConnectionClose()
    {
        KeepClientConnectionOpen = false;
    }

    private void SetRouteAction(string action)
    {
        RouteAction = action;
    }
}
