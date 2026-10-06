namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyListenerSubsystemSummaryResponse(int Configured, int Enabled, int Active, int Failed, int Draining, int Http1Enabled, int Http2Enabled, int Http3Enabled, int QuicReady)
{
}
