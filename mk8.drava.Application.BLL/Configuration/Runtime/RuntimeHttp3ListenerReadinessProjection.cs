namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHttp3ListenerReadinessProjection
{
    public RuntimeHttp3ListenerReadinessProjection(bool Configured, bool DefaultEnabled, string EnablementLevel, bool EnabledForTraffic, string DisabledReason, bool AltSvcConfigured, int AltSvcMaxAgeSeconds, bool UdpQuicListenerIdentityModeled, RuntimeQuicListenerIdentityProjection? QuicIdentity)
    {
        RuntimeHttp3ListenerReadinessFacts.Validate(EnablementLevel, DisabledReason, AltSvcMaxAgeSeconds);
        this.Configured = Configured;
        this.DefaultEnabled = DefaultEnabled;
        this.EnablementLevel = EnablementLevel;
        this.EnabledForTraffic = EnabledForTraffic;
        this.DisabledReason = DisabledReason;
        this.AltSvcConfigured = AltSvcConfigured;
        this.AltSvcMaxAgeSeconds = AltSvcMaxAgeSeconds;
        this.UdpQuicListenerIdentityModeled = UdpQuicListenerIdentityModeled;
        this.QuicIdentity = QuicIdentity;
    }

    public bool Configured { get; }
    public bool DefaultEnabled { get; }
    public string EnablementLevel { get; }
    public bool EnabledForTraffic { get; }
    public string DisabledReason { get; }
    public bool AltSvcConfigured { get; }
    public int AltSvcMaxAgeSeconds { get; }
    public bool UdpQuicListenerIdentityModeled { get; }
    public RuntimeQuicListenerIdentityProjection? QuicIdentity { get; }
}
