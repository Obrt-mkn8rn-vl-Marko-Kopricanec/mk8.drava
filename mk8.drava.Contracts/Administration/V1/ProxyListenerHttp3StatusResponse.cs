namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyListenerHttp3StatusResponse(bool Configured, bool DefaultEnabled, string EnablementLevel, bool EnabledForTraffic, string DisabledReason, bool AltSvcConfigured, int AltSvcMaxAgeSeconds, bool UdpQuicListenerIdentityModeled, ProxyQuicListenerIdentityResponse? QuicIdentity)
{
}
