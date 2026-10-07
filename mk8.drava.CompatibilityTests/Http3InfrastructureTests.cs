using System.Net.Security;
using System.Net.Quic;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Tls;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class Http3InfrastructureTests
{
    public static void ExistingHttp1AndHttp2ProtocolsStillValidate()
    {
        var http1 = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, ValidProxyOptions(new ListenerOptions { Name = "http1", Address = "127.0.0.1", Port = 8080, Transport = "http", Protocols = "http1" }));
        var http2 = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, ValidProxyOptions(new ListenerOptions { Name = "http2", Address = "127.0.0.1", Port = 8443, Transport = "https", Protocols = "http2" }));
        var both = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, ValidProxyOptions(new ListenerOptions { Name = "both", Address = "127.0.0.1", Port = 9443, Transport = "https", Protocols = "http1AndHttp2" }));
        AssertEx.False(http1.Failed, string.Join("; ", http1.Failures ?? []));
        AssertEx.False(http2.Failed, string.Join("; ", http2.Failures ?? []));
        AssertEx.False(both.Failed, string.Join("; ", both.Failures ?? []));
    }

    public static void ListenerProtocolConfigParsingUsesCurrentHttp3Spellings()
    {
        var cases = new (string Text, RuntimeListenerProtocols Protocols)[]
        {
            ("http1", RuntimeListenerProtocols.Http1),
            ("http2", RuntimeListenerProtocols.Http2),
            ("http1AndHttp2", RuntimeListenerProtocols.Http1AndHttp2),
            ("http3", RuntimeListenerProtocols.Http3),
            ("http1AndHttp3", RuntimeListenerProtocols.Http1AndHttp3),
            ("http2AndHttp3", RuntimeListenerProtocols.Http2AndHttp3),
            ("http1AndHttp2AndHttp3", RuntimeListenerProtocols.Http1AndHttp2AndHttp3)
        };
        foreach (var entry in cases)
        {
            var parsed = RuntimeListenerProtocolExtensions.ParseConfigText(entry.Text);
            var compatibilityParsed = RuntimeHttp3Compatibility.ParseProtocols(entry.Text);
            if (parsed is not RuntimeListenerProtocolParseResult.AcceptedResult acceptedProtocols)
            {
                throw new InvalidOperationException($"Expected accepted listener protocol parse for {entry.Text}.");
            }

            if (compatibilityParsed is not RuntimeListenerProtocolParseResult.AcceptedResult acceptedCompatibilityProtocols)
            {
                throw new InvalidOperationException($"Expected accepted compatibility protocol parse for {entry.Text}.");
            }

            AssertEx.Equal(entry.Protocols, acceptedProtocols.Protocols);
            AssertEx.Equal(entry.Protocols, acceptedCompatibilityProtocols.Protocols);
            AssertEx.Equal(entry.Text, acceptedProtocols.Protocols.ToConfigText());
        }

        AssertEx.True(RuntimeListenerProtocols.Http1AndHttp2AndHttp3.HasHttp3());
        AssertEx.Equal(RuntimeListenerProtocolExtensions.SupportedConfigValues.Count, cases.Length);
    }

    public static void Http3CompatibilityNormalizerCentralizesEnablementSemantics()
    {
        var enabled = RuntimeHttp3Compatibility.From(Http3Listener("stable", "http1AndHttp3"));
        var disabled = RuntimeHttp3Compatibility.From(new ListenerOptions { Name = "disabled", Address = "127.0.0.1", Port = 8443, Transport = "https", Protocols = "http3", Http3Enablement = "disabled", DefaultCertificateId = "default" });
        AssertEx.True(enabled.ProtocolsValid);
        AssertEx.True(enabled.EnablementValid);
        AssertEx.Equal(RuntimeHttp3Enablement.Default, enabled.EffectiveEnablement);
        AssertEx.True(enabled.ExplicitHttp3Requested);
        AssertEx.Equal(RuntimeHttp3Enablement.Disabled, disabled.EffectiveEnablement);
        AssertEx.True(disabled.EnablementExplicitlyConfigured);
        AssertEx.Throws<ArgumentException>(() => new RuntimeHttp3Compatibility(Protocols: (RuntimeListenerProtocols)8, ProtocolsValid: true, EffectiveEnablement: RuntimeHttp3Enablement.Default, EnablementValid: true, EnablementExplicitlyConfigured: false, ExplicitHttp3Requested: false));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHttp3Compatibility(Protocols: RuntimeListenerProtocols.Http1, ProtocolsValid: true, EffectiveEnablement: (RuntimeHttp3Enablement)99, EnablementValid: true, EnablementExplicitlyConfigured: false, ExplicitHttp3Requested: false));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHttp3Compatibility(Protocols: RuntimeListenerProtocols.Http1, ProtocolsValid: true, EffectiveEnablement: RuntimeHttp3Enablement.Default, EnablementValid: true, EnablementExplicitlyConfigured: false, ExplicitHttp3Requested: true));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHttp3Compatibility(Protocols: RuntimeListenerProtocols.Http3, ProtocolsValid: false, EffectiveEnablement: RuntimeHttp3Enablement.Default, EnablementValid: true, EnablementExplicitlyConfigured: false, ExplicitHttp3Requested: true));
        AssertEx.Throws<ArgumentException>(() => new RuntimeHttp3Compatibility(Protocols: RuntimeListenerProtocols.Http1, ProtocolsValid: true, EffectiveEnablement: RuntimeHttp3Enablement.Disabled, EnablementValid: false, EnablementExplicitlyConfigured: true, ExplicitHttp3Requested: false));
        AssertEx.Throws<ArgumentException>(() => RuntimeListenerProtocolParseResult.Accepted(RuntimeListenerProtocols.None));
        AssertEx.Throws<ArgumentException>(() => RuntimeHttp3EnablementParseResult.Accepted((RuntimeHttp3Enablement)99, explicitlyConfigured: true));
    }

    public static void CurrentHttp3ConfigValidatesMapsAndAggregatesConsistently()
    {
        var listener = Http3Listener("main", "http1AndHttp2AndHttp3");
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, ValidProxyOptions(listener));
        var operationalOptions = new ProxyOperationalOptions();
        var snapshot = ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(ValidProxyOptions(listener), operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UtcNow, "memory", [], Discovery());
        var aggregated = SiteOptionsAggregator.ToProxyOptions([SiteConfigurationSource.FromFile("stable", new SiteOptions { Name = "stable", Host = "stable.test", Upstreams = [new UpstreamOptions { Name = "local", Address = "127.0.0.1", Port = 5000 }], Listeners = [listener] }), SiteConfigurationSource.FromFile("stable2", new SiteOptions { Name = "stable2", Host = "stable2.test", Upstreams = [new UpstreamOptions { Name = "local", Address = "127.0.0.1", Port = 5001 }], Listeners = [new ListenerOptions { Name = "main", Address = "127.0.0.1", Port = 8443, Transport = "https", Protocols = "http1AndHttp2", DefaultCertificateId = "default" }] })]);
        var aggregateValidation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, aggregated);
        AssertEx.False(validation.Failed, string.Join("; ", validation.Failures ?? []));
        AssertEx.Equal(RuntimeListenerProtocols.Http1AndHttp2AndHttp3, snapshot.Listeners[0].Protocols);
        AssertEx.Equal("default", snapshot.Listeners[0].Http3.EnablementLevel);
        AssertEx.True(snapshot.Listeners[0].Http3.EnabledForTraffic);
        AssertEx.False(aggregateValidation.Failed, string.Join("; ", aggregateValidation.Failures ?? []));
        AssertEx.Equal("http1AndHttp2AndHttp3", aggregated.Listeners[0].Protocols);
    }

    public static void LegacyHttp3ConfigAliasesAreRejected()
    {
        var options = ValidProxyOptions(Http3Listener("main", "http3"));
        options.Listeners.Add(new ListenerOptions { Name = "legacy", Address = "127.0.0.1", Port = 9443, Transport = "https", Protocols = "http3Preview", Http3Enablement = "preview", DefaultCertificateId = "default" });
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, options);
        var failures = AssertEx.NotNull(validation.Failures);
        AssertEx.True(validation.Failed);
        AssertEx.True(failures.Any(static failure => failure.Contains("Protocols must be", StringComparison.Ordinal)), string.Join("; ", failures));
        AssertEx.True(failures.Any(static failure => failure.Contains("Http3Enablement must be", StringComparison.Ordinal)), string.Join("; ", failures));
    }

    public static void RemovedHttp3ConfigPropertiesAreRejectedByParser()
    {
        var parser = new SiteConfigurationParser();
        var experimental = CaptureJsonException(() => parser.ReadSiteText("""
            {
              "name": "site",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": 8443,
                  "transport": "https",
                  "protocols": "http3",
                  "experimentalHttp3": true,
                  "defaultCertificateId": "default"
                }
              ],
              "host": "localhost"
            }
            """, SiteConfigurationFormat.Json));
        var buffered = CaptureJsonException(() => parser.ReadSiteText("""
            {
              "name": "site",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": 8443,
                  "transport": "https",
                  "protocols": "http3",
                  "http3MaxBufferedRequestBodyBytes": 4096,
                  "defaultCertificateId": "default"
                }
              ],
              "host": "localhost"
            }
            """, SiteConfigurationFormat.Json));
        AssertEx.True(experimental.Message.Contains("experimentalHttp3", StringComparison.Ordinal), experimental.Message);
        AssertEx.True(buffered.Message.Contains("http3MaxBufferedRequestBodyBytes", StringComparison.Ordinal), buffered.Message);
    }

    private static System.Text.Json.JsonException CaptureJsonException(Action action)
    {
        try
        {
            action();
        }
        catch (System.Text.Json.JsonException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected JSON exception.");
    }

    public static void Http3DefaultEnabledForEligibleTlsListener()
    {
        var listener = RuntimeListenerFor("http1");
        AssertEx.True(listener.Http3.Configured);
        AssertEx.True(listener.Http3.EnabledForTraffic);
        AssertEx.Equal("default", listener.Http3.EnablementLevel);
        AssertEx.Equal("default_enabled", listener.Http3.DisabledReason);
    }

    public static void Http3DefaultDisabledForPlaintextListener()
    {
        var listener = new RuntimeListener("main", "127.0.0.1", 8080, true, RuntimeListenerTransport.Http, null, [], 512, 32 * 1024, 32 * 1024, 1024, 64 * 1024);
        AssertEx.False(listener.Http3.Configured);
        AssertEx.False(listener.Http3.EnabledForTraffic);
        AssertEx.Equal("tls_required", listener.Http3.DisabledReason);
    }

    public static void Http3ProtocolUsesDefaultEnablement()
    {
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, ValidProxyOptions(Http3Listener("current", "http1AndHttp2AndHttp3")));
        var runtime = RuntimeListenerFor("http1AndHttp2AndHttp3");
        AssertEx.False(validation.Failed, string.Join("; ", validation.Failures ?? []));
        AssertEx.True(runtime.Http3.EnabledForTraffic);
        AssertEx.Equal("default", runtime.Http3.EnablementLevel);
        AssertEx.Equal("default_enabled", runtime.Http3.DisabledReason);
    }

    public static void Http3RequiresTlsCertificateCapableListener()
    {
        var listener = new ListenerOptions
        {
            Name = "current",
            Address = "127.0.0.1",
            Port = 8443,
            Transport = "https",
            Protocols = "http1AndHttp2AndHttp3",
            DefaultCertificateId = null,
            SniCertificates = []
        };
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, ValidProxyOptions(listener));
        AssertEx.True(validation.Failed);
        var failures = AssertEx.NotNull(validation.Failures);
        AssertEx.True(failures.Any(static failure => failure.Contains("requires DefaultCertificateId or SniCertificates", StringComparison.Ordinal)), string.Join("; ", failures));
    }

    public static void Http3ConfigEnablesTraffic()
    {
        var listener = Http3Listener("current", "http1AndHttp2AndHttp3");
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, ValidProxyOptions(listener));
        var runtime = RuntimeListenerFor("http1AndHttp2AndHttp3");
        AssertEx.False(validation.Failed, string.Join("; ", validation.Failures ?? []));
        AssertEx.True(runtime.TcpTrafficEnabled);
        AssertEx.True(runtime.Http3.EnabledForTraffic);
        AssertEx.Equal("default_enabled", runtime.Http3.DisabledReason);
    }

    public static void Http3OnlyDoesNotEnableTcpTraffic()
    {
        var listener = Http3Listener("current", "http3");
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, ValidProxyOptions(listener));
        var runtime = RuntimeListenerFor("http3");
        AssertEx.False(validation.Failed, string.Join("; ", validation.Failures ?? []));
        AssertEx.False(runtime.TcpTrafficEnabled);
        AssertEx.True(runtime.Http3.EnabledForTraffic);
    }

    public static void TcpListenerIdentityRemainsUnchanged()
    {
        var listener = RuntimeListenerFor("http1AndHttp2");
        var identity = listener.Identity;
        AssertEx.Equal("main", identity.Key);
        AssertEx.Equal("127.0.0.1|8443|https", identity.BindKey);
    }

    public static void QuicListenerIdentityIsSeparateFromTcpIdentity()
    {
        var listener = RuntimeListenerFor("http1AndHttp2AndHttp3");
        var tcpIdentity = listener.Identity;
        var quicIdentity = AssertEx.NotNull(listener.QuicIdentity);
        AssertEx.Equal("main|quic", quicIdentity.Key);
        AssertEx.Equal("127.0.0.1|8443|udp|quic", quicIdentity.BindKey);
        AssertEx.False(string.Equals(tcpIdentity.BindKey, quicIdentity.BindKey, StringComparison.Ordinal));
    }

    public static void TcpAlpnDoesNotAdvertiseHttp3()
    {
        var listener = RuntimeListenerFor("http1AndHttp2AndHttp3");
        var protocols = listener.Protocols;
        var tcpInput = ListenerProtocolAdvertisementInputMapper.FromTcpRuntimeProtocols(protocols);
        var quicInput = ListenerProtocolAdvertisementInputMapper.FromHttp3RuntimeListener(listener);
        var tcpPolicy = ListenerProtocolAdvertisementPolicy.BuildTcpAlpnProtocolNames(tcpInput);
        var quicPolicy = ListenerProtocolAdvertisementPolicy.BuildHttp3AlpnProtocolNames(quicInput);
        var tcpAlpn = ListenerProtocolAdvertisement.BuildTcpAlpn(protocols);
        var quicAlpn = ListenerProtocolAdvertisement.BuildHttp3Alpn(listener);
        AssertEx.True(tcpInput.Http1Enabled);
        AssertEx.True(tcpInput.Http2Enabled);
        AssertEx.True(quicInput.EnabledForTraffic);
        AssertEx.True(tcpPolicy.Contains(ListenerProtocolAdvertisementPolicy.Http2Alpn, StringComparer.Ordinal));
        AssertEx.True(tcpPolicy.Contains(ListenerProtocolAdvertisementPolicy.Http1Alpn, StringComparer.Ordinal));
        AssertEx.False(tcpPolicy.Contains(ListenerProtocolAdvertisementPolicy.Http3Alpn, StringComparer.Ordinal));
        AssertEx.True(quicPolicy.Contains(ListenerProtocolAdvertisementPolicy.Http3Alpn, StringComparer.Ordinal));
        AssertEx.True(tcpAlpn.Contains(SslApplicationProtocol.Http2));
        AssertEx.True(tcpAlpn.Contains(SslApplicationProtocol.Http11));
        AssertEx.False(tcpAlpn.Any(static protocol => protocol.Protocol.Span.SequenceEqual("h3"u8)));
        AssertEx.True(quicAlpn.Any(static protocol => protocol.Protocol.Span.SequenceEqual("h3"u8)));
    }

    public static void StatusAndEffectiveProjectionReportHttp3Enabled()
    {
        var operationalOptions = new ProxyOperationalOptions();
        var snapshot = ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(ValidProxyOptions(Http3Listener("current", "http1AndHttp2AndHttp3")), operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UtcNow, "memory", [], Discovery());
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot));
        AssertEx.Equal("default", projection.Http3.Configured);
        AssertEx.True(projection.Http3.EnabledForTraffic);
        AssertEx.Equal("default_enabled", projection.Http3.DisabledReason);
        AssertEx.True(projection.Http3.UdpQuicListenerIdentityModeled);
        var statusProjection = Http3RuntimeSupport.ProjectConfiguration(ProxyHttp3SupportConfigurationSourceMapper.FromSources(snapshot.Listeners.Select(static listener => listener), snapshot.Routes.Select(static route => route)), TestHttp3PlatformSupport.Supported);
        AssertEx.Equal("default", statusProjection.Configured);
        AssertEx.True(statusProjection.EnabledForTraffic);
        AssertEx.Equal("default_enabled", statusProjection.DisabledReason);
        AssertEx.False(statusProjection.DefaultReadinessBlockers.Contains("qpack_dynamic_table_unsupported", StringComparer.Ordinal));
        AssertEx.False(statusProjection.DefaultReadinessBlockers.Contains("request_body_buffered_not_streamed", StringComparer.Ordinal));
        AssertEx.Equal("static_with_zero_dynamic_table", statusProjection.QpackMode);
        AssertEx.Equal("streaming", statusProjection.RequestBodyMode);
    }

    public static void FinalSupportProjectionReportsHttp3MatrixAndFinalNaming()
    {
        var options = ValidProxyOptions(Http3Listener("main", "http1AndHttp2"));
        options.Routes[0].Upstreams[0] = new UpstreamOptions
        {
            Name = "h3",
            Scheme = "https",
            Protocol = "http3",
            Address = "upstream.test",
            Port = 443
        };
        var operationalOptions = new ProxyOperationalOptions();
        var snapshot = ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(options, operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UtcNow, "memory", [], Discovery());
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot)).Http3;
        AssertEx.Equal("default_enabled_for_eligible_tls_proxy_listeners", projection.ClientHttp3SupportLevel);
        AssertEx.Equal("opt_in_https_quic_reused_multiplexed", projection.UpstreamHttp3SupportLevel);
        AssertEx.True(projection.ClientProtocols.SequenceEqual(["http1", "http2", "http3"], StringComparer.Ordinal));
        AssertEx.True(projection.UpstreamProtocols.SequenceEqual(["http1", "http2", "http3"], StringComparer.Ordinal));
        AssertEx.True(projection.SupportedRouteActions.Contains("proxy", StringComparer.Ordinal));
        AssertEx.True(projection.SupportedRouteActions.Contains("redirect", StringComparer.Ordinal));
        AssertEx.True(projection.SupportedRouteActions.Contains("staticResponse", StringComparer.Ordinal));
        AssertEx.True(projection.SupportedRouteActions.Contains("maintenance", StringComparer.Ordinal));
        AssertEx.True(projection.SupportedPolicyFeatures.Contains("cache_get_head", StringComparer.Ordinal));
        AssertEx.True(projection.SupportedPolicyFeatures.Contains("retry_circuit_safe_methods", StringComparer.Ordinal));
        AssertEx.True(projection.SupportedPolicyFeatures.Contains("weighted_balancing", StringComparer.Ordinal));
        AssertEx.True(projection.SupportedPolicyFeatures.Contains("health_checks", StringComparer.Ordinal));
        AssertEx.True(projection.UnsupportedFeatures.SequenceEqual(RuntimeHttp3UnsupportedFeatureCodes.EffectiveConfig, StringComparer.Ordinal));
        AssertEx.False(projection.UnsupportedFeatures.Contains("upstream_http3_multiplexing", StringComparer.Ordinal));
        AssertEx.Equal("reused_multiplexed", projection.UpstreamPoolingMode);
        AssertEx.True(projection.UpstreamMultiplexingEnabled);
        AssertEx.Equal(8, projection.UpstreamMaxStreamsPerConnection);
        AssertEx.Equal("", projection.UpstreamPoolingLimitationReason);
        if (QuicListener.IsSupported && QuicConnection.IsSupported)
        {
            AssertEx.Equal("default-enabled", projection.DefaultEnablementState);
            AssertEx.Equal("default_enabled_for_eligible_tls_proxy_listeners", projection.ReadinessConclusion);
        }
    }

    public static void Http3SupportSourcesAndProjectionCopyInputLists()
    {
        AssertEx.Throws<ArgumentNullException>(() => _ = new Http3SupportConfigurationSource(null!, UpstreamHttp3Configured: false));
        AssertEx.Throws<ArgumentNullException>(() => _ = new Http3SupportConfigurationSource([null!], UpstreamHttp3Configured: false));
        AssertEx.Throws<ArgumentNullException>(() => Http3RuntimeSupport.ProjectConfiguration(null!, TestHttp3PlatformSupport.Supported));
        AssertEx.Throws<ArgumentNullException>(() => Http3RuntimeSupport.ProjectConfiguration(Http3SupportConfigurationSource.Empty, null!));
        AssertEx.Throws<ArgumentNullException>(() => Http3RuntimeSupport.ProjectRuntime(Http3SupportConfigurationSource.Empty, TestHttp3PlatformSupport.Supported, null!));
        AssertEx.Throws<ArgumentNullException>(() => Http3RuntimeSupport.ProjectRuntime(Http3SupportConfigurationSource.Empty, TestHttp3PlatformSupport.Supported, [null!]));
        AssertEx.Throws<ArgumentNullException>(() => Http3SupportSourceMapper.FromListenerStatuses([null!]));
        var operationalOptions = new ProxyOperationalOptions();
        var snapshot = ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(ValidProxyOptions(Http3Listener("source", "http1AndHttp3")), operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UtcNow, "memory", [], Discovery());
        AssertEx.Throws<ArgumentNullException>(() => ProxyHttp3SupportConfigurationSourceMapper.FromSources(null!, snapshot.Routes));
        AssertEx.Throws<ArgumentNullException>(() => ProxyHttp3SupportConfigurationSourceMapper.FromSources(snapshot.Listeners, null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyHttp3SupportConfigurationSourceMapper.FromSources([null!], snapshot.Routes));
        AssertEx.Throws<ArgumentNullException>(() => ProxyHttp3SupportConfigurationSourceMapper.FromSources(snapshot.Listeners, [null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyHttp3SupportConfigurationSourceMapper.FromSources(snapshot.Listeners, [snapshot.Routes[0].WithUpstreams([null!])]));
        var listener = new Http3SupportListenerSource(Configured: true, EnabledForTraffic: true, EnablementLevel: "default", AltSvcEnabled: true, AltSvcMaxAgeSeconds: 3600, QuicListenerIdentity: "main|quic");
        var listeners = new List<Http3SupportListenerSource>
        {
            listener
        };
        var source = new Http3SupportConfigurationSource(Listeners: listeners.Select(static listener => listener), UpstreamHttp3Configured: true);
        var blockers = new List<string>
        {
            "runtime_quic_unsupported"
        };
        var clientProtocols = new List<string>
        {
            "http1",
            "http2",
            "http3"
        };
        var upstreamProtocols = new List<string>
        {
            "http1",
            "http3"
        };
        var routeActions = new List<string>
        {
            "proxy"
        };
        var policyFeatures = new List<string>
        {
            "cache_get_head"
        };
        var unsupported = new List<string>
        {
            "webtransport_over_http3"
        };
        var projection = new RuntimeHttp3SupportProjection(RuntimeSupport: "supported", QuicListenerSupported: true, QuicConnectionSupported: true, Configured: "default", EnablementLevel: "default", EnabledForTraffic: true, QuicListenerReady: true, AltSvcConfigured: true, AltSvcActive: true, AltSvcMaxAgeSeconds: 3600, DisabledReason: "quic_listener_ready", UdpQuicListenerIdentityModeled: true, ReadinessConclusion: "default_enabled_for_eligible_tls_proxy_listeners", DefaultEnablementState: "default-enabled", DefaultReadinessBlockers: blockers, AltSvcStateReason: "active", QpackMode: "static_with_zero_dynamic_table", QpackDynamicTableCapacity: 0, QpackBlockedStreams: 0, RequestBodyMode: "streaming", ClientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", UpstreamHttp3SupportLevel: "opt_in_https_quic_reused_multiplexed", ClientProtocols: clientProtocols, UpstreamProtocols: upstreamProtocols, SupportedRouteActions: routeActions, SupportedPolicyFeatures: policyFeatures, UnsupportedFeatures: unsupported, UpstreamHttp3Configured: true, UpstreamPoolingMode: "reused_multiplexed", UpstreamMultiplexingEnabled: true, UpstreamMaxStreamsPerConnection: 8, UpstreamQpackMode: "static_with_zero_dynamic_table", UpstreamPoolingLimitationReason: "");
        listeners[0] = listener with
        {
            QuicListenerIdentity = "replacement|quic"
        };
        blockers[0] = "replacement_blocker";
        clientProtocols[0] = "replacement_client";
        upstreamProtocols[0] = "replacement_upstream";
        routeActions[0] = "replacement_action";
        policyFeatures[0] = "replacement_policy";
        unsupported[0] = "replacement_unsupported";
        listeners.Clear();
        blockers.Clear();
        clientProtocols.Clear();
        upstreamProtocols.Clear();
        routeActions.Clear();
        policyFeatures.Clear();
        unsupported.Clear();
        AssertEx.Equal(1, source.Listeners.Count);
        AssertEx.Equal("main|quic", source.Listeners[0].QuicListenerIdentity);
        AssertEx.Equal("runtime_quic_unsupported", projection.DefaultReadinessBlockers[0]);
        AssertEx.Equal("http1", projection.ClientProtocols[0]);
        AssertEx.Equal("http1", projection.UpstreamProtocols[0]);
        AssertEx.Equal("proxy", projection.SupportedRouteActions[0]);
        AssertEx.Equal("cache_get_head", projection.SupportedPolicyFeatures[0]);
        AssertEx.Equal("webtransport_over_http3", projection.UnsupportedFeatures[0]);
        AssertEx.False(source.Listeners is Http3SupportListenerSource[], "HTTP/3 configuration source listeners should not expose a mutable array.");
        AssertEx.False(projection.ClientProtocols is string[], "HTTP/3 projection protocol lists should not expose mutable arrays.");
        AssertEx.False(projection.UnsupportedFeatures is string[], "HTTP/3 projection unsupported-feature lists should not expose mutable arrays.");
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHttp3SupportProjection(RuntimeSupport: null!, QuicListenerSupported: true, QuicConnectionSupported: true, Configured: "default", EnablementLevel: "default", EnabledForTraffic: true, QuicListenerReady: true, AltSvcConfigured: true, AltSvcActive: true, AltSvcMaxAgeSeconds: 3600, DisabledReason: "quic_listener_ready", UdpQuicListenerIdentityModeled: true, ReadinessConclusion: "default_enabled_for_eligible_tls_proxy_listeners", DefaultEnablementState: "default-enabled", DefaultReadinessBlockers: [], AltSvcStateReason: "active", QpackMode: "static_with_zero_dynamic_table", QpackDynamicTableCapacity: 0, QpackBlockedStreams: 0, RequestBodyMode: "streaming", ClientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", UpstreamHttp3SupportLevel: "opt_in_https_quic_reused_multiplexed", ClientProtocols: [], UpstreamProtocols: [], SupportedRouteActions: [], SupportedPolicyFeatures: [], UnsupportedFeatures: [], UpstreamHttp3Configured: true, UpstreamPoolingMode: "reused_multiplexed", UpstreamMultiplexingEnabled: true, UpstreamMaxStreamsPerConnection: 8, UpstreamQpackMode: "static_with_zero_dynamic_table", UpstreamPoolingLimitationReason: ""));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHttp3SupportProjection(RuntimeSupport: "supported", QuicListenerSupported: true, QuicConnectionSupported: true, Configured: "default", EnablementLevel: "default", EnabledForTraffic: true, QuicListenerReady: true, AltSvcConfigured: true, AltSvcActive: true, AltSvcMaxAgeSeconds: 3600, DisabledReason: "quic_listener_ready", UdpQuicListenerIdentityModeled: true, ReadinessConclusion: "default_enabled_for_eligible_tls_proxy_listeners", DefaultEnablementState: "default-enabled", DefaultReadinessBlockers: null!, AltSvcStateReason: "active", QpackMode: "static_with_zero_dynamic_table", QpackDynamicTableCapacity: 0, QpackBlockedStreams: 0, RequestBodyMode: "streaming", ClientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", UpstreamHttp3SupportLevel: "opt_in_https_quic_reused_multiplexed", ClientProtocols: [], UpstreamProtocols: [], SupportedRouteActions: [], SupportedPolicyFeatures: [], UnsupportedFeatures: [], UpstreamHttp3Configured: true, UpstreamPoolingMode: "reused_multiplexed", UpstreamMultiplexingEnabled: true, UpstreamMaxStreamsPerConnection: 8, UpstreamQpackMode: "static_with_zero_dynamic_table", UpstreamPoolingLimitationReason: ""));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHttp3SupportProjection(RuntimeSupport: "supported", QuicListenerSupported: true, QuicConnectionSupported: true, Configured: "default", EnablementLevel: "default", EnabledForTraffic: true, QuicListenerReady: true, AltSvcConfigured: true, AltSvcActive: true, AltSvcMaxAgeSeconds: 3600, DisabledReason: "quic_listener_ready", UdpQuicListenerIdentityModeled: true, ReadinessConclusion: "default_enabled_for_eligible_tls_proxy_listeners", DefaultEnablementState: "default-enabled", DefaultReadinessBlockers: [], AltSvcStateReason: "active", QpackMode: "static_with_zero_dynamic_table", QpackDynamicTableCapacity: 0, QpackBlockedStreams: 0, RequestBodyMode: "streaming", ClientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", UpstreamHttp3SupportLevel: "opt_in_https_quic_reused_multiplexed", ClientProtocols: null!, UpstreamProtocols: [], SupportedRouteActions: [], SupportedPolicyFeatures: [], UnsupportedFeatures: [], UpstreamHttp3Configured: true, UpstreamPoolingMode: "reused_multiplexed", UpstreamMultiplexingEnabled: true, UpstreamMaxStreamsPerConnection: 8, UpstreamQpackMode: "static_with_zero_dynamic_table", UpstreamPoolingLimitationReason: ""));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHttp3SupportProjection(RuntimeSupport: "supported", QuicListenerSupported: true, QuicConnectionSupported: true, Configured: "default", EnablementLevel: "default", EnabledForTraffic: true, QuicListenerReady: true, AltSvcConfigured: true, AltSvcActive: true, AltSvcMaxAgeSeconds: 3600, DisabledReason: "quic_listener_ready", UdpQuicListenerIdentityModeled: true, ReadinessConclusion: "default_enabled_for_eligible_tls_proxy_listeners", DefaultEnablementState: "default-enabled", DefaultReadinessBlockers: [], AltSvcStateReason: "active", QpackMode: "static_with_zero_dynamic_table", QpackDynamicTableCapacity: 0, QpackBlockedStreams: 0, RequestBodyMode: "streaming", ClientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", UpstreamHttp3SupportLevel: "opt_in_https_quic_reused_multiplexed", ClientProtocols: [], UpstreamProtocols: [], SupportedRouteActions: [], SupportedPolicyFeatures: [], UnsupportedFeatures: null!, UpstreamHttp3Configured: true, UpstreamPoolingMode: "reused_multiplexed", UpstreamMultiplexingEnabled: true, UpstreamMaxStreamsPerConnection: 8, UpstreamQpackMode: "static_with_zero_dynamic_table", UpstreamPoolingLimitationReason: ""));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHttp3SupportProjection(RuntimeSupport: "supported", QuicListenerSupported: true, QuicConnectionSupported: true, Configured: "default", EnablementLevel: "default", EnabledForTraffic: true, QuicListenerReady: true, AltSvcConfigured: true, AltSvcActive: true, AltSvcMaxAgeSeconds: 3600, DisabledReason: "quic_listener_ready", UdpQuicListenerIdentityModeled: true, ReadinessConclusion: "default_enabled_for_eligible_tls_proxy_listeners", DefaultEnablementState: "default-enabled", DefaultReadinessBlockers: [null!], AltSvcStateReason: "active", QpackMode: "static_with_zero_dynamic_table", QpackDynamicTableCapacity: 0, QpackBlockedStreams: 0, RequestBodyMode: "streaming", ClientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", UpstreamHttp3SupportLevel: "opt_in_https_quic_reused_multiplexed", ClientProtocols: [], UpstreamProtocols: [], SupportedRouteActions: [], SupportedPolicyFeatures: [], UnsupportedFeatures: [], UpstreamHttp3Configured: true, UpstreamPoolingMode: "reused_multiplexed", UpstreamMultiplexingEnabled: true, UpstreamMaxStreamsPerConnection: 8, UpstreamQpackMode: "static_with_zero_dynamic_table", UpstreamPoolingLimitationReason: ""));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeHttp3SupportProjection(RuntimeSupport: "supported", QuicListenerSupported: true, QuicConnectionSupported: true, Configured: "default", EnablementLevel: "default", EnabledForTraffic: true, QuicListenerReady: true, AltSvcConfigured: true, AltSvcActive: true, AltSvcMaxAgeSeconds: 3600, DisabledReason: "quic_listener_ready", UdpQuicListenerIdentityModeled: true, ReadinessConclusion: "default_enabled_for_eligible_tls_proxy_listeners", DefaultEnablementState: "default-enabled", DefaultReadinessBlockers: [], AltSvcStateReason: "active", QpackMode: "static_with_zero_dynamic_table", QpackDynamicTableCapacity: 0, QpackBlockedStreams: 0, RequestBodyMode: "streaming", ClientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", UpstreamHttp3SupportLevel: "opt_in_https_quic_reused_multiplexed", ClientProtocols: [null!], UpstreamProtocols: [], SupportedRouteActions: [], SupportedPolicyFeatures: [], UnsupportedFeatures: [], UpstreamHttp3Configured: true, UpstreamPoolingMode: "reused_multiplexed", UpstreamMultiplexingEnabled: true, UpstreamMaxStreamsPerConnection: 8, UpstreamQpackMode: "static_with_zero_dynamic_table", UpstreamPoolingLimitationReason: ""));
        var response = RuntimeHttp3SupportResponseMapper.FromProjection(projection);
        AssertEx.Equal("runtime_quic_unsupported", response.DefaultReadinessBlockers[0]);
        AssertEx.Equal("http1", response.ClientProtocols[0]);
        AssertEx.Equal("http1", response.UpstreamProtocols[0]);
        AssertEx.Equal("proxy", response.SupportedRouteActions[0]);
        AssertEx.Equal("cache_get_head", response.SupportedPolicyFeatures[0]);
        AssertEx.Equal("webtransport_over_http3", response.UnsupportedFeatures[0]);
        AssertEx.False(ReferenceEquals(projection.DefaultReadinessBlockers, response.DefaultReadinessBlockers), "HTTP/3 API blockers should not reuse the BLL projection list.");
        AssertEx.False(ReferenceEquals(projection.ClientProtocols, response.ClientProtocols), "HTTP/3 API client protocols should not reuse the BLL projection list.");
        AssertEx.False(response.ClientProtocols is string[], "HTTP/3 API protocol lists should not expose mutable arrays.");
        AssertEx.False(response.UnsupportedFeatures is string[], "HTTP/3 API unsupported-feature lists should not expose mutable arrays.");
        var directBlockers = new List<string>
        {
            "direct_blocker"
        };
        var directClientProtocols = new List<string>
        {
            "direct_http1"
        };
        var directUpstreamProtocols = new List<string>
        {
            "direct_h3"
        };
        var directRouteActions = new List<string>
        {
            "direct_proxy"
        };
        var directPolicyFeatures = new List<string>
        {
            "direct_cache"
        };
        var directUnsupported = new List<string>
        {
            "direct_webtransport"
        };
        var directResponse = new RuntimeHttp3SupportResponse(runtimeSupport: "supported", quicListenerSupported: true, quicConnectionSupported: true, configured: "default", enablementLevel: "default", enabledForTraffic: true, quicListenerReady: true, altSvcConfigured: true, altSvcActive: true, altSvcMaxAgeSeconds: 3600, disabledReason: "quic_listener_ready", udpQuicListenerIdentityModeled: true, readinessConclusion: "ready", defaultEnablementState: "enabled", defaultReadinessBlockers: directBlockers, altSvcStateReason: "active", qpackMode: "static_with_zero_dynamic_table", qpackDynamicTableCapacity: 0, qpackBlockedStreams: 0, requestBodyMode: "streaming", clientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", upstreamHttp3SupportLevel: "opt_in_https_quic_reused_multiplexed", clientProtocols: directClientProtocols, upstreamProtocols: directUpstreamProtocols, supportedRouteActions: directRouteActions, supportedPolicyFeatures: directPolicyFeatures, unsupportedFeatures: directUnsupported, upstreamHttp3Configured: true, upstreamPoolingMode: "reused_multiplexed", upstreamMultiplexingEnabled: true, upstreamMaxStreamsPerConnection: 8, upstreamQpackMode: "static_with_zero_dynamic_table", upstreamPoolingLimitationReason: "");
        directBlockers[0] = "replacement_direct_blocker";
        directClientProtocols[0] = "replacement_direct_client";
        directUpstreamProtocols[0] = "replacement_direct_upstream";
        directRouteActions[0] = "replacement_direct_action";
        directPolicyFeatures[0] = "replacement_direct_policy";
        directUnsupported[0] = "replacement_direct_unsupported";
        directBlockers.Clear();
        directClientProtocols.Clear();
        directUpstreamProtocols.Clear();
        directRouteActions.Clear();
        directPolicyFeatures.Clear();
        directUnsupported.Clear();
        AssertEx.Throws<ArgumentNullException>(() => _ = new RuntimeHttp3SupportResponse(runtimeSupport: "supported", quicListenerSupported: true, quicConnectionSupported: true, configured: "default", enablementLevel: "default", enabledForTraffic: true, quicListenerReady: true, altSvcConfigured: true, altSvcActive: true, altSvcMaxAgeSeconds: 3600, disabledReason: "quic_listener_ready", udpQuicListenerIdentityModeled: true, readinessConclusion: "ready", defaultEnablementState: "enabled", defaultReadinessBlockers: directBlockers, altSvcStateReason: "active", qpackMode: "static_with_zero_dynamic_table", qpackDynamicTableCapacity: 0, qpackBlockedStreams: 0, requestBodyMode: "streaming", clientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", upstreamHttp3SupportLevel: "opt_in_https_quic_reused_multiplexed", clientProtocols: directClientProtocols, upstreamProtocols: directUpstreamProtocols, supportedRouteActions: directRouteActions, supportedPolicyFeatures: directPolicyFeatures, unsupportedFeatures: null!, upstreamHttp3Configured: true, upstreamPoolingMode: "reused_multiplexed", upstreamMultiplexingEnabled: true, upstreamMaxStreamsPerConnection: 8, upstreamQpackMode: "static_with_zero_dynamic_table", upstreamPoolingLimitationReason: ""));
        AssertEx.Equal("direct_blocker", directResponse.DefaultReadinessBlockers[0]);
        AssertEx.Equal("direct_http1", directResponse.ClientProtocols[0]);
        AssertEx.Equal("direct_h3", directResponse.UpstreamProtocols[0]);
        AssertEx.Equal("direct_proxy", directResponse.SupportedRouteActions[0]);
        AssertEx.Equal("direct_cache", directResponse.SupportedPolicyFeatures[0]);
        AssertEx.Equal("direct_webtransport", directResponse.UnsupportedFeatures[0]);
        AssertEx.False(directResponse.ClientProtocols is string[], "Direct HTTP/3 API protocol lists should not expose mutable arrays.");
        AssertEx.False(directResponse.UnsupportedFeatures is string[], "Direct HTTP/3 API unsupported-feature lists should not expose mutable arrays.");
    }

    public static void UpstreamProtocolAcceptsExplicitHttp3()
    {
        var options = ValidProxyOptions(new ListenerOptions { Name = "main", Address = "127.0.0.1", Port = 8080, Transport = "http", Protocols = "http1" });
        options.Routes[0].Upstreams[0] = new UpstreamOptions
        {
            Name = "h3",
            Scheme = "https",
            Protocol = "http3",
            Address = "upstream.test",
            Port = 443
        };
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, options);
        AssertEx.False(validation.Failed, string.Join("; ", validation.Failures ?? []));
    }

    private static ListenerOptions Http3Listener(string name, string protocols)
    {
        return new ListenerOptions
        {
            Name = name,
            Address = "127.0.0.1",
            Port = 8443,
            Transport = "https",
            Protocols = protocols,
            DefaultCertificateId = "default"
        };
    }

    private static RuntimeListener RuntimeListenerFor(string protocols)
    {
        return new RuntimeListener("main", "127.0.0.1", 8443, true, RuntimeListenerTransport.Https, "default", [], 512, 32 * 1024, 32 * 1024, 1024, 64 * 1024, ParseProtocols(protocols), RuntimeHttp3Enablement.Default, RuntimeHttp3AltSvcOptions.Disabled, RuntimeHttp2Limits.Default);
    }

    private static RuntimeListenerProtocols ParseProtocols(string protocols)
    {
        return RuntimeListenerProtocolExtensions.ParseConfigTextOrDefault(protocols);
    }

    private static ProxyOptions ValidProxyOptions(ListenerOptions listener)
    {
        return new ProxyOptions
        {
            Listeners = [listener],
            Routes = [new ProxyRouteOptions
            {
                Name = "proxy",
                Host = "*",
                PathPrefix = "/",
                Upstreams = [new UpstreamOptions
                {
                    Name = "local",
                    Address = "127.0.0.1",
                    Port = 5000
                }

                ]
            }

            ]
        };
    }

    private static ProxyConfigurationDiscovery Discovery()
    {
        return new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []);
    }
}
