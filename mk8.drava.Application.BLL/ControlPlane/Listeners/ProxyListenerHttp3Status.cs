namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed record ProxyListenerHttp3Status(bool Configured, bool DefaultEnabled, string EnablementLevel, bool EnabledForTraffic, string DisabledReason, bool AltSvcConfigured, int AltSvcMaxAgeSeconds, bool UdpQuicListenerIdentityModeled, ProxyQuicListenerIdentity? QuicIdentity);
