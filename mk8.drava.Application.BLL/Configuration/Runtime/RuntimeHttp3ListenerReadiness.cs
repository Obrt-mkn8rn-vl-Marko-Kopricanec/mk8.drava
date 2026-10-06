namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHttp3ListenerReadiness
{
    public RuntimeHttp3ListenerReadiness(bool Configured, bool DefaultEnabled, string EnablementLevel, bool EnabledForTraffic, string DisabledReason, bool AltSvcConfigured, int AltSvcMaxAgeSeconds, bool UdpQuicListenerIdentityModeled, RuntimeQuicListenerIdentity? QuicIdentity)
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
    public RuntimeQuicListenerIdentity? QuicIdentity { get; }

    public static RuntimeHttp3ListenerReadiness From(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        var certificateCapable = !string.IsNullOrWhiteSpace(listener.DefaultCertificateId) || listener.SniCertificates.Count > 0;
        var enablement = RuntimeHttp3Compatibility.ResolveEffectiveEnablement(listener.Http3Enablement);
        var configured = enablement != RuntimeHttp3Enablement.Disabled && listener.Transport == RuntimeListenerTransport.Https && certificateCapable;
        var enabledForTraffic = configured && enablement != RuntimeHttp3Enablement.Disabled && listener.Transport == RuntimeListenerTransport.Https && certificateCapable;
        var reason = DisabledReasonFor(listener, configured, certificateCapable, enablement, enabledForTraffic);
        return new RuntimeHttp3ListenerReadiness(configured, enablement == RuntimeHttp3Enablement.Default, enablement.ToConfigText(), enabledForTraffic, reason, enabledForTraffic || listener.Http3AltSvc.Enabled, listener.Http3AltSvc.MaxAgeSeconds, configured, enabledForTraffic ? RuntimeQuicListenerIdentity.From(listener) : null);
    }

    private static string DisabledReasonFor(RuntimeListener listener, bool configured, bool certificateCapable, RuntimeHttp3Enablement enablement, bool enabledForTraffic)
    {
        if (enabledForTraffic)
        {
            return enablement switch
            {
                _ => "default_enabled"
            };
        }

        if (enablement == RuntimeHttp3Enablement.Disabled)
        {
            return "disabled";
        }

        if (listener.Transport != RuntimeListenerTransport.Https)
        {
            return "tls_required";
        }

        if (!certificateCapable)
        {
            return "certificate_required";
        }

        return configured ? "configured_but_inactive" : "not_configured";
    }
}
