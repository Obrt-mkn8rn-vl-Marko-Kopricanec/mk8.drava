namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public sealed record RuntimeHttp3SupportProjection
{
    public RuntimeHttp3SupportProjection(string RuntimeSupport, bool QuicListenerSupported, bool QuicConnectionSupported, string Configured, string EnablementLevel, bool EnabledForTraffic, bool QuicListenerReady, bool AltSvcConfigured, bool AltSvcActive, int? AltSvcMaxAgeSeconds, string DisabledReason, bool UdpQuicListenerIdentityModeled, string ReadinessConclusion) : this(RuntimeSupport, QuicListenerSupported, QuicConnectionSupported, Configured, EnablementLevel, EnabledForTraffic, QuicListenerReady, AltSvcConfigured, AltSvcActive, AltSvcMaxAgeSeconds, DisabledReason, UdpQuicListenerIdentityModeled, ReadinessConclusion, DefaultEnablementState: "disabled", DefaultReadinessBlockers: [], AltSvcStateReason: "not_configured", QpackMode: "static_with_zero_dynamic_table", QpackDynamicTableCapacity: 0, QpackBlockedStreams: 0, RequestBodyMode: "streaming", ClientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", UpstreamHttp3SupportLevel: "opt_in_https_quic", ClientProtocols: ["http1", "http2", "http3"], UpstreamProtocols: ["http1", "http2", "http3"], SupportedRouteActions: ["proxy", "redirect", "staticResponse", "maintenance"], SupportedPolicyFeatures: ["cache_get_head", "retry_circuit_safe_methods", "weighted_balancing", "health_checks", "path_rewrites", "canonical_redirects", "http_to_https_redirects", "forwarded_headers", "request_response_header_policies"], UnsupportedFeatures: RuntimeHttp3UnsupportedFeatureCodes.EffectiveConfig, UpstreamHttp3Configured: false, UpstreamPoolingMode: "not_configured", UpstreamMultiplexingEnabled: false, UpstreamMaxStreamsPerConnection: 8, UpstreamQpackMode: "static_with_zero_dynamic_table", UpstreamPoolingLimitationReason: "")
    {
    }

    public RuntimeHttp3SupportProjection(string RuntimeSupport, bool QuicListenerSupported, bool QuicConnectionSupported, string Configured, string EnablementLevel, bool EnabledForTraffic, bool QuicListenerReady, bool AltSvcConfigured, bool AltSvcActive, int? AltSvcMaxAgeSeconds, string DisabledReason, bool UdpQuicListenerIdentityModeled, string ReadinessConclusion, string DefaultEnablementState, IReadOnlyList<string> DefaultReadinessBlockers, string AltSvcStateReason, string QpackMode, int QpackDynamicTableCapacity, int QpackBlockedStreams, string RequestBodyMode, string ClientHttp3SupportLevel, string UpstreamHttp3SupportLevel, IReadOnlyList<string> ClientProtocols, IReadOnlyList<string> UpstreamProtocols, IReadOnlyList<string> SupportedRouteActions, IReadOnlyList<string> SupportedPolicyFeatures, IReadOnlyList<string> UnsupportedFeatures, bool UpstreamHttp3Configured, string UpstreamPoolingMode, bool UpstreamMultiplexingEnabled, int UpstreamMaxStreamsPerConnection, string UpstreamQpackMode, string UpstreamPoolingLimitationReason)
    {
        ValidateRequiredText(RuntimeSupport, Configured, EnablementLevel, DisabledReason, ReadinessConclusion, DefaultEnablementState, AltSvcStateReason, QpackMode, RequestBodyMode, ClientHttp3SupportLevel, UpstreamHttp3SupportLevel, UpstreamPoolingMode, UpstreamQpackMode, UpstreamPoolingLimitationReason);
        this.RuntimeSupport = RuntimeSupport;
        this.QuicListenerSupported = QuicListenerSupported;
        this.QuicConnectionSupported = QuicConnectionSupported;
        this.Configured = Configured;
        this.EnablementLevel = EnablementLevel;
        this.EnabledForTraffic = EnabledForTraffic;
        this.QuicListenerReady = QuicListenerReady;
        this.AltSvcConfigured = AltSvcConfigured;
        this.AltSvcActive = AltSvcActive;
        this.AltSvcMaxAgeSeconds = AltSvcMaxAgeSeconds;
        this.DisabledReason = DisabledReason;
        this.UdpQuicListenerIdentityModeled = UdpQuicListenerIdentityModeled;
        this.ReadinessConclusion = ReadinessConclusion;
        this.DefaultEnablementState = DefaultEnablementState;
        this.DefaultReadinessBlockers = Http3List.Copy(DefaultReadinessBlockers);
        this.AltSvcStateReason = AltSvcStateReason;
        this.QpackMode = QpackMode;
        this.QpackDynamicTableCapacity = QpackDynamicTableCapacity;
        this.QpackBlockedStreams = QpackBlockedStreams;
        this.RequestBodyMode = RequestBodyMode;
        this.ClientHttp3SupportLevel = ClientHttp3SupportLevel;
        this.UpstreamHttp3SupportLevel = UpstreamHttp3SupportLevel;
        this.ClientProtocols = Http3List.Copy(ClientProtocols);
        this.UpstreamProtocols = Http3List.Copy(UpstreamProtocols);
        this.SupportedRouteActions = Http3List.Copy(SupportedRouteActions);
        this.SupportedPolicyFeatures = Http3List.Copy(SupportedPolicyFeatures);
        this.UnsupportedFeatures = Http3List.Copy(UnsupportedFeatures);
        this.UpstreamHttp3Configured = UpstreamHttp3Configured;
        this.UpstreamPoolingMode = UpstreamPoolingMode;
        this.UpstreamMultiplexingEnabled = UpstreamMultiplexingEnabled;
        this.UpstreamMaxStreamsPerConnection = UpstreamMaxStreamsPerConnection;
        this.UpstreamQpackMode = UpstreamQpackMode;
        this.UpstreamPoolingLimitationReason = UpstreamPoolingLimitationReason;
    }

    private static void ValidateRequiredText(string RuntimeSupport, string Configured, string EnablementLevel, string DisabledReason, string ReadinessConclusion, string DefaultEnablementState, string AltSvcStateReason, string QpackMode, string RequestBodyMode, string ClientHttp3SupportLevel, string UpstreamHttp3SupportLevel, string UpstreamPoolingMode, string UpstreamQpackMode, string UpstreamPoolingLimitationReason)
    {
        ArgumentNullException.ThrowIfNull(RuntimeSupport);
        ArgumentNullException.ThrowIfNull(Configured);
        ArgumentNullException.ThrowIfNull(EnablementLevel);
        ArgumentNullException.ThrowIfNull(DisabledReason);
        ArgumentNullException.ThrowIfNull(ReadinessConclusion);
        ArgumentNullException.ThrowIfNull(DefaultEnablementState);
        ArgumentNullException.ThrowIfNull(AltSvcStateReason);
        ArgumentNullException.ThrowIfNull(QpackMode);
        ArgumentNullException.ThrowIfNull(RequestBodyMode);
        ArgumentNullException.ThrowIfNull(ClientHttp3SupportLevel);
        ArgumentNullException.ThrowIfNull(UpstreamHttp3SupportLevel);
        ArgumentNullException.ThrowIfNull(UpstreamPoolingMode);
        ArgumentNullException.ThrowIfNull(UpstreamQpackMode);
        ArgumentNullException.ThrowIfNull(UpstreamPoolingLimitationReason);
    }

    public string RuntimeSupport { get; }
    public bool QuicListenerSupported { get; }
    public bool QuicConnectionSupported { get; }
    public string Configured { get; }
    public string EnablementLevel { get; }
    public bool EnabledForTraffic { get; }
    public bool QuicListenerReady { get; }
    public bool AltSvcConfigured { get; }
    public bool AltSvcActive { get; }
    public int? AltSvcMaxAgeSeconds { get; }
    public string DisabledReason { get; }
    public bool UdpQuicListenerIdentityModeled { get; }
    public string ReadinessConclusion { get; }
    public string DefaultEnablementState { get; }
    public IReadOnlyList<string> DefaultReadinessBlockers { get; }
    public string AltSvcStateReason { get; }
    public string QpackMode { get; }
    public int QpackDynamicTableCapacity { get; }
    public int QpackBlockedStreams { get; }
    public string RequestBodyMode { get; }
    public string ClientHttp3SupportLevel { get; }
    public string UpstreamHttp3SupportLevel { get; }
    public IReadOnlyList<string> ClientProtocols { get; }
    public IReadOnlyList<string> UpstreamProtocols { get; }
    public IReadOnlyList<string> SupportedRouteActions { get; }
    public IReadOnlyList<string> SupportedPolicyFeatures { get; }
    public IReadOnlyList<string> UnsupportedFeatures { get; }
    public bool UpstreamHttp3Configured { get; }
    public string UpstreamPoolingMode { get; }
    public bool UpstreamMultiplexingEnabled { get; }
    public int UpstreamMaxStreamsPerConnection { get; }
    public string UpstreamQpackMode { get; }
    public string UpstreamPoolingLimitationReason { get; }
}
