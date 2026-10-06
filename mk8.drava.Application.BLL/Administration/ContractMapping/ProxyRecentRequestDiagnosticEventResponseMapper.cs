using BusinessProxyRecentRequestDiagnosticEvent = Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics.ProxyRecentRequestDiagnosticEvent;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyRecentRequestDiagnosticEventResponseMapper
{
    public static IReadOnlyList<ProxyRecentRequestDiagnosticEventResponse> FromEvents(IReadOnlyList<BusinessProxyRecentRequestDiagnosticEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return ApiResponseList.Copy(events.Select(FromEvent));
    }

    private static ProxyRecentRequestDiagnosticEventResponse FromEvent(BusinessProxyRecentRequestDiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        return new ProxyRecentRequestDiagnosticEventResponse(diagnosticEvent.TimestampUtc, diagnosticEvent.RequestId, diagnosticEvent.ExternalRequestId, diagnosticEvent.ConfigVersion, diagnosticEvent.ListenerName, diagnosticEvent.Transport, diagnosticEvent.ClientEndpoint, diagnosticEvent.Method, diagnosticEvent.Host, diagnosticEvent.Target, diagnosticEvent.RouteName, diagnosticEvent.UpstreamName, diagnosticEvent.UpstreamEndpoint, diagnosticEvent.ResponseStatusCode, diagnosticEvent.DurationMilliseconds, diagnosticEvent.FailureKind, diagnosticEvent.ResponseStarted, diagnosticEvent.KeepClientConnectionOpen, diagnosticEvent.IsUpgrade, diagnosticEvent.TunnelEstablished, diagnosticEvent.TunnelCloseReason, diagnosticEvent.TunnelBytesClientToUpstream, diagnosticEvent.TunnelBytesUpstreamToClient);
    }
}
