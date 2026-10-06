namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyRecentRequestDiagnosticEventResponse(DateTimeOffset TimestampUtc, string RequestId, string? ExternalRequestId, int ConfigVersion, string ListenerName, string Transport, string? ClientEndpoint, string? Method, string? Host, string? Target, string? RouteName, string? UpstreamName, string? UpstreamEndpoint, int? ResponseStatusCode, long DurationMilliseconds, string FailureKind, bool ResponseStarted, bool KeepClientConnectionOpen, bool IsUpgrade, bool TunnelEstablished, string? TunnelCloseReason, long TunnelBytesClientToUpstream, long TunnelBytesUpstreamToClient)
{
}
