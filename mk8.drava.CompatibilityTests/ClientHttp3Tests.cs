#pragma warning disable CA1416
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static partial class ClientHttp3Tests
{
    private static readonly SslApplicationProtocol Http3Alpn = new("h3");
    public static void Http3DefaultEnabledForEligibleTlsListener()
    {
        var listener = new RuntimeListener("main", "127.0.0.1", 8443, true, RuntimeListenerTransport.Https, "default", [], 512, 32 * 1024, 32 * 1024, 1024, 64 * 1024);
        AssertEx.True(listener.Http3.Configured);
        AssertEx.True(listener.Http3.EnabledForTraffic);
        AssertEx.Equal("default", listener.Http3.EnablementLevel);
        AssertEx.Equal("default_enabled", listener.Http3.DisabledReason);
    }

    public static void ExplicitHttp3DisablePreventsTraffic()
    {
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, new ProxyOptions { Listeners = [new ListenerOptions { Name = "main", Address = "127.0.0.1", Port = 8443, Transport = "https", Protocols = "http1AndHttp2", Http3Enablement = "disabled", DefaultCertificateId = "default" }], Routes = [new ProxyRouteOptions { Name = "static", Host = "*", PathPrefix = "/", Action = "staticResponse" }] });
        var listener = new RuntimeListener("main", "127.0.0.1", 8443, true, RuntimeListenerTransport.Https, "default", [], 512, 32 * 1024, 32 * 1024, 1024, 64 * 1024, RuntimeListenerProtocols.Http1, RuntimeHttp3Enablement.Disabled, RuntimeHttp3AltSvcOptions.Disabled, RuntimeHttp2Limits.Default);
        AssertEx.False(validation.Failed, string.Join("; ", validation.Failures ?? []));
        AssertEx.False(listener.Http3.Configured);
        AssertEx.False(listener.Http3.EnabledForTraffic);
        AssertEx.Equal("disabled", listener.Http3.DisabledReason);
    }

    public static void QuicListenerIdentityIsSeparateFromTcpIdentity()
    {
        var listener = TestHttp3Listener("http1AndHttp2AndHttp3");
        var tcp = listener.Identity;
        var quic = AssertEx.NotNull(listener.QuicIdentity);
        AssertEx.Equal("main", tcp.Key);
        AssertEx.Equal("main|quic", quic.Key);
        AssertEx.False(string.Equals(tcp.BindKey, quic.BindKey, StringComparison.Ordinal));
    }

    public static async Task FailedQuicListenerStartDoesNotBreakTcpListenerAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http1AndHttp3", staticBody: "unused");
        using var host = BuildProxyHost(temp.Path, services => services.AddSingleton<IHttp3QuicListenerFactory>(static _ => new FailingQuicListenerFactory()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "tcp", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Failed, timeout.Token).ConfigureAwait(false);
            var snapshot = runtime.Snapshot();
            AssertEx.True(snapshot.IsRunning);
            AssertEx.True(snapshot.Listeners.Any(static listener => string.Equals(listener.Kind, "tcp", StringComparison.Ordinal) && listener.State == ProxyListenerState.Active));
            AssertEx.True(snapshot.Listeners.Any(static listener => string.Equals(listener.Kind, "quic", StringComparison.Ordinal) && listener.State == ProxyListenerState.Failed));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task DefaultHttp3TlsListenerStartsQuicAndEmitsAltSvcAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http1AndHttp2", staticBody: "default-h3", altSvcMaxAgeSeconds: 60, http3EnablementOverride: "default");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "tcp", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var response = await SendHttp1TlsRequestAsync(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "/alt", timeout.Token).ConfigureAwait(false);
            var status = new ProxyStatusController(host.Services.GetRequiredService<ProxyStatusAdministrationService>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            }.Get();
            AssertEx.True(response.Contains($"Alt-Svc: h3=\":{port}\"; ma=60", StringComparison.Ordinal), response);
            AssertEx.Equal("default", status.Http3.Configured);
            AssertEx.True(status.Http3.EnabledForTraffic);
            AssertEx.True(status.Http3.AltSvcActive);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task SuccessfulReloadCanAddAndRemoveQuicListenerAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http1", staticBody: "unused");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "tcp", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            WriteHttp3Site(temp.Path, port, "http1AndHttp3", staticBody: "unused");
            var add = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            WriteHttp3Site(temp.Path, port, "http1", staticBody: "unused");
            var remove = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            await WaitForNoListenerAsync(runtime, "main", "quic", timeout.Token).ConfigureAwait(false);
            var addReload = ProxyConfigurationReloadResultAssertions.Reloaded(add, string.Join("; ", add.Errors));
            var removeReload = ProxyConfigurationReloadResultAssertions.Reloaded(remove, string.Join("; ", remove.Errors));
            AssertEx.True(addReload.ListenerReload.Added >= 1);
            AssertEx.True(removeReload.ListenerReload.Removed >= 1);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task FailedReloadPreservesOldQuicListenerSetAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http1AndHttp3", staticBody: "live");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            var before = await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "sites", "broken.json"), "{ nope").ConfigureAwait(false);
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var after = await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            ProxyConfigurationReloadResultAssertions.Failed(reload);
            AssertEx.Equal(before.StartedAtUtc, after.StartedAtUtc);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task SuccessfulHttp3CertificateReloadKeepsQuicListenerAndUsesNewCertificateAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http1AndHttp3", staticBody: "cert-live");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            var before = await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var beforeSubject = "";
            var activeConnection = (await ConnectHttp3Async(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), timeout.Token, subject => beforeSubject = subject).ConfigureAwait(false));
            await using var activeConnectionDisposal = activeConnection.ConfigureAwait(false);
            {
                var beforeStream = (await activeConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeout.Token).ConfigureAwait(false));
                await using var beforeStreamDisposal = beforeStream.ConfigureAwait(false);
                await WriteHttp3RequestAsync(beforeStream, "GET", "/before-cert", null, timeout.Token).ConfigureAwait(false);
                var beforeResponse = DecodeHttp3Response(await ReadToEndAsync(beforeStream, timeout.Token).ConfigureAwait(false));
                AssertEx.Equal("200", HeaderValue(beforeResponse.Headers, ":status"));
                AssertEx.Equal("cert-live", beforeResponse.Body);
            }

            TestCertificates.WriteSelfSignedPfx(Path.Combine(temp.Path, "certs", "home.pfx"), "localhost-reloaded", "secret");
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var after = await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            {
                var activeAfterStream = (await activeConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeout.Token).ConfigureAwait(false));
                await using var activeAfterStreamDisposal = activeAfterStream.ConfigureAwait(false);
                await WriteHttp3RequestAsync(activeAfterStream, "GET", "/after-cert-active", null, timeout.Token).ConfigureAwait(false);
                var activeAfterResponse = DecodeHttp3Response(await ReadToEndAsync(activeAfterStream, timeout.Token).ConfigureAwait(false));
                AssertEx.Equal("200", HeaderValue(activeAfterResponse.Headers, ":status"));
                AssertEx.Equal("cert-live", activeAfterResponse.Body);
            }

            var afterSubject = "";
            var afterResponse = await SendHttp3RequestAsync(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret", allowNameMismatch: true), "GET", "/after-cert", timeout.Token, certificateSubjectObserver: subject => afterSubject = subject).ConfigureAwait(false);
            ProxyConfigurationReloadResultAssertions.Reloaded(reload, string.Join("; ", reload.Errors));
            AssertEx.Equal("200", HeaderValue(afterResponse.Headers, ":status"));
            AssertEx.Equal("cert-live", afterResponse.Body);
            AssertEx.True(beforeSubject.Contains("CN=localhost", StringComparison.Ordinal), beforeSubject);
            AssertEx.True(afterSubject.Contains("CN=localhost-reloaded", StringComparison.Ordinal), afterSubject);
            AssertEx.Equal(before.StartedAtUtc, after.StartedAtUtc);
            await activeConnection.CloseAsync(0, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task FailedHttp3CertificateReloadPreservesPreviousQuicCertificateAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        var originalCertificate = TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret");
        WriteHttp3Site(temp.Path, port, "http1AndHttp3", staticBody: "cert-live");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            var before = await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var beforeSubject = "";
            var beforeResponse = await SendHttp3RequestAsync(port, originalCertificate, "GET", "/before-failed-cert", timeout.Token, certificateSubjectObserver: subject => beforeSubject = subject).ConfigureAwait(false);
            TestCertificates.WriteSelfSignedPfx(Path.Combine(temp.Path, "certs", "home.pfx"), "localhost-reloaded", "secret");
            await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "sites", "broken.json"), "{ nope").ConfigureAwait(false);
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var after = await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var afterSubject = "";
            var afterResponse = await SendHttp3RequestAsync(port, originalCertificate, "GET", "/after-failed-cert", timeout.Token, certificateSubjectObserver: subject => afterSubject = subject).ConfigureAwait(false);
            ProxyConfigurationReloadResultAssertions.Failed(reload);
            AssertEx.Equal("200", HeaderValue(beforeResponse.Headers, ":status"));
            AssertEx.Equal("200", HeaderValue(afterResponse.Headers, ":status"));
            AssertEx.True(beforeSubject.Contains("CN=localhost", StringComparison.Ordinal), beforeSubject);
            AssertEx.True(afterSubject.Contains("CN=localhost", StringComparison.Ordinal), afterSubject);
            AssertEx.Equal(before.StartedAtUtc, after.StartedAtUtc);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static void StatusAndEffectiveConfigUseCurrentHttp3Projection()
    {
        var operationalOptions = new ProxyOperationalOptions();
        var snapshot = ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(new ProxyOptions { Listeners = [new ListenerOptions { Name = "main", Address = "127.0.0.1", Port = 8443, Transport = "https", Protocols = "http3", DefaultCertificateId = "default" }], Routes = [new ProxyRouteOptions { Name = "static", Host = "*", PathPrefix = "/", Action = "staticResponse" }] }, operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UtcNow, "memory", [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("data", "config", "sites", "logs", "certs", "state", "proxy.json"), [], [], []));
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot));
        AssertEx.Equal("default", projection.Http3.Configured);
        AssertEx.True(projection.Http3.EnabledForTraffic);
        AssertEx.Equal("default_enabled", projection.Http3.DisabledReason);
        AssertEx.False(projection.Http3.DefaultReadinessBlockers.Contains("qpack_dynamic_table_unsupported", StringComparer.Ordinal));
        AssertEx.False(projection.Http3.DefaultReadinessBlockers.Contains("request_body_buffered_not_streamed", StringComparer.Ordinal));
        AssertEx.Equal("static_with_zero_dynamic_table", projection.Http3.QpackMode);
        AssertEx.Equal(0, projection.Http3.QpackDynamicTableCapacity);
        AssertEx.Equal(0, projection.Http3.QpackBlockedStreams);
        AssertEx.Equal("streaming", projection.Http3.RequestBodyMode);
        AssertEx.True(snapshot.Listeners[0].Http3.DefaultEnabled);
    }

    public static void Http3LegacyEnablementValuesAreRejected()
    {
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, new ProxyOptions { Listeners = [new ListenerOptions { Name = "main", Address = "127.0.0.1", Port = 8443, Transport = "https", Protocols = "http3", Http3Enablement = "preview", DefaultCertificateId = "default" }], Routes = [new ProxyRouteOptions { Name = "static", Host = "*", PathPrefix = "/", Action = "staticResponse" }] });
        var failures = AssertEx.NotNull(validation.Failures);
        AssertEx.True(validation.Failed);
        AssertEx.True(failures.Any(static failure => failure.Contains("Http3Enablement must be", StringComparison.Ordinal)), string.Join("; ", failures));
    }

    public static void AltSvcPolicyReadsNarrowRuntimeListenerSource()
    {
        var listener = TestHttp3Listener("http1AndHttp3", new RuntimeHttp3AltSvcOptions(Enabled: true, MaxAgeSeconds: 60));
        var source = new FixedHttp3AltSvcRuntimeListenerSource([ActiveQuicListenerStatus(listener)]);
        var metrics = new ProxyMetrics();
        var policy = new Http3AltSvcPolicy(source, metrics);
        var result = policy.CreateHeader(new Http3AltSvcListenerInput(listener.Http3.EnabledForTraffic, listener.Http3.EnablementLevel, listener.Http3AltSvc.Enabled, listener.Http3AltSvc.MaxAgeSeconds, listener.Port, listener.QuicIdentity?.Key));
        var snapshot = metrics.Snapshot();
        AssertEx.True(result is Http3AltSvcHeaderResult.EmittedResult);
        var header = ((Http3AltSvcHeaderResult.EmittedResult)result).Header;
        AssertEx.Equal("Alt-Svc", header.Name);
        AssertEx.Equal("h3=\":8443\"; ma=60", header.Value);
        AssertEx.Equal(1, source.ReadCount);
        AssertEx.Equal(1L, snapshot.Http3.AltSvcEmitted);
        AssertEx.Equal(0L, snapshot.Http3.AltSvcSuppressed);
    }

    public static void Http3RuntimeMappersRejectNullInputs()
    {
        var listener = TestHttp3Listener("http1AndHttp3", new RuntimeHttp3AltSvcOptions(Enabled: true, MaxAgeSeconds: 60));
        AssertEx.Throws<ArgumentNullException>(() => ProxyHttp3AltSvcRuntimeMapper.ToListenerInput(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyHttp3RequestTranslationRuntimeMapper.ToListenerInput(null!));
        var altSvc = ProxyHttp3AltSvcRuntimeMapper.ToListenerInput(listener);
        var translation = ProxyHttp3RequestTranslationRuntimeMapper.ToListenerInput(listener);
        AssertEx.True(altSvc.EnabledForTraffic);
        AssertEx.Equal("default", altSvc.EnablementLevel);
        AssertEx.True(altSvc.AltSvcEnabled);
        AssertEx.Equal(60, altSvc.AltSvcMaxAgeSeconds);
        AssertEx.Equal(8443, altSvc.Port);
        AssertEx.Equal("main|quic", altSvc.QuicListenerIdentity);
        AssertEx.True(translation.IsHttps);
    }

    public static void AltSvcPolicyAppliesHeaderWithoutKeepingStaleValues()
    {
        var result = Http3AltSvcPolicy.ApplyHeader([new ProxyHeaderField("Content-Type", "text/plain"), new ProxyHeaderField("Alt-Svc", "h3=\":443\"; ma=1")], Http3AltSvcHeaderResult.Emitted(new ProxyHeaderField("Alt-Svc", "h3=\":8443\"; ma=60")));
        AssertEx.Equal(2, result.Count);
        AssertEx.True(result.Any(static header => string.Equals(header.Name, "Content-Type", StringComparison.Ordinal) && string.Equals(header.Value, "text/plain", StringComparison.Ordinal)));
        AssertEx.Equal("h3=\":8443\"; ma=60", result.Single(static header => string.Equals(header.Name, "Alt-Svc", StringComparison.OrdinalIgnoreCase)).Value);
        var suppressed = Http3AltSvcPolicy.ApplyHeader(result, Http3AltSvcHeaderResult.Suppressed);
        AssertEx.Equal(1, suppressed.Count);
        AssertEx.False(suppressed.Any(static header => string.Equals(header.Name, "Alt-Svc", StringComparison.OrdinalIgnoreCase)));
    }

    public static async Task AltSvcIsAbsentWhenHttp3ExplicitlyDisabledAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http1", staticBody: "alt-disabled");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "tcp", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var response = await SendHttp1TlsRequestAsync(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "/alt", timeout.Token).ConfigureAwait(false);
            AssertEx.False(response.Contains("Alt-Svc:", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task AltSvcIsEmittedOnlyWhenConfiguredAndReadyAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http1AndHttp3", staticBody: "alt-ready", altSvcEnabled: true, altSvcMaxAgeSeconds: 60);
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var response = await SendHttp1TlsRequestAsync(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "/alt", timeout.Token).ConfigureAwait(false);
            var status = new ProxyStatusController(host.Services.GetRequiredService<ProxyStatusAdministrationService>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            }.Get();
            AssertEx.True(response.Contains($"Alt-Svc: h3=\":{port}\"; ma=60", StringComparison.Ordinal), response);
            AssertEx.True(status.Http3.QuicListenerReady);
            AssertEx.True(status.Http3.AltSvcActive);
            AssertEx.Equal("default_enabled_for_eligible_tls_proxy_listeners", status.Http3.ReadinessConclusion);
            AssertEx.Equal("active", status.Http3.AltSvcStateReason);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task AltSvcIsNotEmittedWhenQuicListenerIsNotReadyAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http1AndHttp3", staticBody: "alt-failed", altSvcEnabled: true);
        using var host = BuildProxyHost(temp.Path, services => services.AddSingleton<IHttp3QuicListenerFactory>(static _ => new FailingQuicListenerFactory()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "tcp", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Failed, timeout.Token).ConfigureAwait(false);
            var response = await SendHttp1TlsRequestAsync(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "/alt", timeout.Token).ConfigureAwait(false);
            AssertEx.False(response.Contains("Alt-Svc:", StringComparison.OrdinalIgnoreCase), response);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static void AdminResponsesDoNotEmitAltSvc()
    {
        using var temp = TemporaryDirectory.Create();
        WriteCertificateConfig(temp.Path);
        using var host = BuildProxyHost(temp.Path);
        var controller = new ProxyStatusController(host.Services.GetRequiredService<ProxyStatusAdministrationService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        _ = controller.Get();
        AssertEx.False(controller.Response.Headers.ContainsKey("Alt-Svc"));
    }

    public static async Task MinimalHttp3GetCanReachGeneratedRouteAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/hello?x=1", "hello-h3").ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("200", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("hello-h3", result.Body);
        AssertEx.True(result.Metrics.Http3.AcceptedConnections >= 1);
        AssertEx.True(result.Metrics.Http3.Requests >= 1);
    }

    public static async Task HeadReturnsHeadersWithoutBodyAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("HEAD", "/head", "head-body").ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("200", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("9", HeaderValue(result.Headers, "content-length"));
        AssertEx.Equal("", result.Body);
    }

    public static async Task Http3GeneratedRedirectRouteWorksAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/old?id=1", "unused", routeJson: """
                {
                  "name": "redirect",
                  "pathPrefix": "/old",
                  "action": "redirect",
                  "redirect": {
                    "statusCode": 308,
                    "targetPath": "/new",
                    "preserveQuery": true
                  }
                }
            """).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("308", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("/new?id=1", HeaderValue(result.Headers, "location"));
        AssertEx.Equal("", result.Body);
    }

    public static async Task Http3GeneratedMaintenanceRouteWorksAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/maintenance", "unused", routeJson: """
                {
                  "name": "maintenance",
                  "pathPrefix": "/maintenance",
                  "action": "proxy",
                  "maintenance": {
                    "enabled": true,
                    "retryAfterSeconds": 120,
                    "body": "maintenance-h3"
                  },
                  "upstreams": [
                    {
                      "name": "unused",
                      "address": "127.0.0.1",
                      "port": 65535
                    }
                  ]
                }
            """).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("503", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("120", HeaderValue(result.Headers, "retry-after"));
        AssertEx.Equal("maintenance-h3", result.Body);
    }

    public static async Task Http3RouteMissReturnsSafe404Async()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/missing", "unused", routeJson: """
                {
                  "name": "known",
                  "pathPrefix": "/known",
                  "action": "staticResponse",
                  "staticResponse": {
                    "statusCode": 200,
                    "contentType": "text/plain",
                    "body": "known"
                  }
                }
            """).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("404", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("Not Found", result.Body);
    }

    public static async Task Http3RouteMissRemainsStableAcrossRepeatedReadyListenerRequestsAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http3", "unused", routeJson: """
                {
                  "name": "known",
                  "pathPrefix": "/known",
                  "action": "staticResponse",
                  "staticResponse": {
                    "statusCode": 200,
                    "contentType": "text/plain",
                    "body": "known"
                  }
                }
            """);
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            for (var index = 0; index < 3; index++)
            {
                var response = await SendHttp3RequestAsync(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "GET", $"/missing-{index}", timeout.Token).ConfigureAwait(false);
                AssertEx.Equal("404", HeaderValue(response.Headers, ":status"));
                AssertEx.Equal("Not Found", response.Body);
            }
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task Http3GetProxyRouteWorksAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3ProxyRouteScenarioAsync("GET", "/proxy", "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 9\r\n\r\nh3-proxy").ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("200", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("h3-proxy", result.Body);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /proxy HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
        AssertEx.True(result.Metrics.Http3.ProxiedRequests >= 1);
    }

    public static async Task Http3HeadProxyRouteWorksAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3ProxyRouteScenarioAsync("HEAD", "/head", "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 9\r\nX-Head: yes\r\n\r\nh3-proxy").ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("200", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("yes", HeaderValue(result.Headers, "x-head"));
        AssertEx.Equal("", result.Body);
        AssertEx.True(result.UpstreamRequest.StartsWith("HEAD /head HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task Http3ProxyPreservesQueryStringAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3ProxyRouteScenarioAsync("GET", "/search?q=one&sort=two", "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nok").ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /search?q=one&sort=two HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task Http3ProxyStripsPseudoHeadersBeforeUpstreamAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3ProxyRouteScenarioAsync("GET", "/headers", "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nok").ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.False(result.UpstreamRequest.Contains(":method", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(result.UpstreamRequest.Contains(":scheme", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(result.UpstreamRequest.Contains(":authority", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(result.UpstreamRequest.Contains(":path", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task Http3ResponseHeadersAreEncodedSafelyAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3ProxyRouteScenarioAsync("GET", "/safe-headers", "HTTP/1.1 200 OK\r\nConnection: close\r\nKeep-Alive: timeout=5\r\nContent-Length: 2\r\nX-Safe: yes\r\n\r\nok").ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("200", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("yes", HeaderValue(result.Headers, "x-safe"));
        AssertEx.False(HeaderExists(result.Headers, "connection"));
        AssertEx.False(HeaderExists(result.Headers, "keep-alive"));
    }

    public static async Task Http3ChunkedResponseStreamsBodyWithoutTransferEncodingAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3ProxyRouteScenarioAsync("GET", "/chunked", "HTTP/1.1 200 OK\r\nConnection: close\r\nTransfer-Encoding: chunked\r\nX-Mode: chunked\r\n\r\n4\r\nwiki\r\n5\r\npedia\r\n0\r\nX-Trailer: ignored\r\n\r\n").ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("200", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("chunked", HeaderValue(result.Headers, "x-mode"));
        AssertEx.Equal("wikipedia", result.Body);
        AssertEx.False(HeaderExists(result.Headers, "transfer-encoding"));
        AssertEx.True(result.Metrics.Http3.StreamedResponses >= 1);
        AssertEx.True(result.Metrics.Http3.ResponseBytesSent >= "wikipedia".Length);
    }

    public static async Task Http3ResponseStreamsBeforeUpstreamCompletesAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpUdpPort();
        var upstreamPort = GetFreeTcpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3ProxySite(temp.Path, proxyPort, upstreamPort);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var firstChunkSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpstream = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamTask = RunStreamingResponseUpstreamAsync(upstreamPort, firstChunkSent, releaseUpstream, timeout.Token);
        var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var connection = (await ConnectHttp3Async(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), timeout.Token).ConfigureAwait(false));
            await using var connectionDisposal = connection.ConfigureAwait(false);
            var stream = (await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeout.Token).ConfigureAwait(false));
            await using var streamDisposal = stream.ConfigureAwait(false);
            await WriteHttp3RequestAsync(stream, "GET", "/stream", null, timeout.Token).ConfigureAwait(false);
            await firstChunkSent.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            var firstData = await ReadFirstHttp3DataAsync(stream, timeout.Token).ConfigureAwait(false);
            releaseUpstream.SetResult();
            _ = await ReadHttp3ResponseRemainderAsync(stream, timeout.Token).ConfigureAwait(false);
            var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var responseMetrics = host.Services.GetRequiredService<ProxyMetrics>();
            await WaitForSuccessfulHttp3RequestCompletionAsync(responseMetrics, timeout.Token).ConfigureAwait(false);
            var metrics = responseMetrics.Snapshot();
            AssertEx.Equal("stream-", firstData);
            AssertEx.True(upstreamRequest.StartsWith("GET /stream HTTP/1.1", StringComparison.Ordinal), upstreamRequest);
            AssertEx.Equal(0L, metrics.Http3.ActiveResponseStreams);
        }
        finally
        {
            releaseUpstream.TrySetResult();
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
            temp.Dispose();
        }
    }

    public static async Task Http3CompletionObserverRequiresRequestOutcomeAsync()
    {
        var metrics = new ProxyMetrics();
        using var cancellation = new CancellationTokenSource();
        var completion = WaitForSuccessfulHttp3RequestCompletionAsync(metrics, cancellation.Token);
        try
        {
            AssertEx.Equal(0L, metrics.Snapshot().Http3.ActiveResponseStreams);
            AssertEx.False(completion.IsCompleted, "An idle response gauge is not a request completion signal.");
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            var canceledWait = false;
            try
            {
                await completion.ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                canceledWait = true;
            }

            AssertEx.True(canceledWait, "The owned completion observer must settle after cancellation.");
        }
    }

    public static async Task Http3CompletionObserverPreservesLeakedResponseGaugeAsync()
    {
        var metrics = new ProxyMetrics();
        metrics.Http3ResponseStreamStarted();
        metrics.Http3RequestCompleted("GET", 200, "success");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await WaitForSuccessfulHttp3RequestCompletionAsync(metrics, timeout.Token).ConfigureAwait(false);
        AssertEx.Equal(1L, metrics.Snapshot().Http3.ActiveResponseStreams);
    }

    public static async Task Http3CacheInteractionUsesStoredResponseAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpUdpPort();
        var upstreamPort = GetFreeTcpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3ProxySite(temp.Path, proxyPort, upstreamPort, """
                  "cache": {
                    "enabled": true,
                    "maxEntryBytes": 4096,
                    "maxTotalBytes": 8192,
                    "defaultTtlSeconds": 60,
                    "respectOriginCacheControl": false
                  },
            """);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = RunSingleResponseUpstreamAsync(upstreamPort, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 6\r\n\r\ncached", timeout.Token);
        var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var first = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "GET", "/cache?x=1", timeout.Token).ConfigureAwait(false);
            var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var second = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "GET", "/cache?x=1", timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            AssertEx.Equal("cached", first.Body);
            AssertEx.Equal("cached", second.Body);
            AssertEx.True(upstreamRequest.StartsWith("GET /cache?x=1 HTTP/1.1", StringComparison.Ordinal), upstreamRequest);
            AssertEx.True(metrics.Http3.ProxiedRequests >= 1);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
            temp.Dispose();
        }
    }

    public static async Task Http3OversizedCacheCandidateStreamsButIsNotCachedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpUdpPort();
        var upstreamPort = GetFreeTcpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3ProxySite(temp.Path, proxyPort, upstreamPort, """
                  "cache": {
                    "enabled": true,
                    "maxEntryBytes": 4,
                    "maxTotalBytes": 8192,
                    "defaultTtlSeconds": 60,
                    "respectOriginCacheControl": false
                  },
            """);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = RunSequentialResponseUpstreamAsync(upstreamPort, ["HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 11\r\n\r\nfirst-large", "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 10\r\n\r\nsecond-big"], timeout.Token);
        var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var first = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "GET", "/large", timeout.Token).ConfigureAwait(false);
            var second = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "GET", "/large", timeout.Token).ConfigureAwait(false);
            var upstreamRequests = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            AssertEx.Equal("first-large", first.Body);
            AssertEx.Equal("second-big", second.Body);
            AssertEx.Equal(2, upstreamRequests.Length);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
            temp.Dispose();
        }
    }

    public static async Task Http3RetryForGetCanReachSecondUpstreamAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpUdpPort();
        var firstUpstreamPort = GetFreeTcpPort();
        var secondUpstreamPort = GetFreeTcpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3RetrySite(temp.Path, proxyPort, firstUpstreamPort, secondUpstreamPort);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = RunSingleResponseUpstreamAsync(secondUpstreamPort, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 5\r\n\r\nretry", timeout.Token);
        var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var response = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "GET", "/retry", timeout.Token).ConfigureAwait(false);
            var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            AssertEx.Equal("200", HeaderValue(response.Headers, ":status"));
            AssertEx.Equal("retry", response.Body);
            AssertEx.True(upstreamRequest.StartsWith("GET /retry HTTP/1.1", StringComparison.Ordinal), upstreamRequest);
            AssertEx.True(metrics.Resilience.RetryAttempts >= 1);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
            temp.Dispose();
        }
    }

    public static async Task UnsupportedConnectIsRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteRawHeadersScenarioAsync([new ProxyHeaderField(":method", "CONNECT"), new ProxyHeaderField(":authority", "upstream.test:443")]).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("501", HeaderValue(result.Headers, ":status"));
        AssertEx.True(result.Metrics.Http3.RejectedRequests.ContainsKey("connect_unsupported"));
        AssertEx.Equal("", result.UpstreamRequest);
    }

    public static async Task MalformedHttp3ConnectIsRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteRawHeadersScenarioAsync([new ProxyHeaderField(":method", "CONNECT"), new ProxyHeaderField(":authority", "not/a/tunnel")]).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("400", HeaderValue(result.Headers, ":status"));
        AssertEx.True(result.Metrics.Http3.ProtocolErrors.ContainsKey("invalid_connect_target"));
    }

    public static async Task ExtendedHttp3ConnectWebSocketIsRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        foreach (var protocol in new[]
        {
            "websocket",
            "connect-udp",
            "webtransport"
        }

        )
        {
            var result = await RunHttp3GeneratedRouteRawHeadersScenarioAsync([new ProxyHeaderField(":method", "CONNECT"), new ProxyHeaderField(":scheme", "https"), new ProxyHeaderField(":authority", "localhost"), new ProxyHeaderField(":path", "/chat"), new ProxyHeaderField(":protocol", protocol)]).ConfigureAwait(false);
            await using var resultDisposal = result.ConfigureAwait(false);
            AssertEx.Equal("400", HeaderValue(result.Headers, ":status"));
            AssertEx.True(result.Metrics.Http3.ProtocolErrors.ContainsKey("extended_connect_unsupported"));
            AssertEx.Equal("", result.UpstreamRequest);
        }
    }

    public static async Task Http3PostWithBoundedBodyReachesUpstreamAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3ProxyRouteScenarioAsync("POST", "/submit?x=1", "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nok", requestBody: "hello=world").ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("200", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("ok", result.Body);
        AssertEx.True(result.UpstreamRequest.StartsWith("POST /submit?x=1 HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("Content-Length: 11", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.EndsWith("hello=world", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task Http3PutPatchAndDeleteBodiesReachUpstreamAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpUdpPort();
        var upstreamPort = GetFreeTcpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3ProxySite(temp.Path, proxyPort, upstreamPort);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = RunSequentialResponseUpstreamAsync(upstreamPort, ["HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 3\r\n\r\nput", "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 5\r\n\r\npatch", "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 6\r\n\r\ndelete"], timeout.Token);
        var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var put = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "PUT", "/items/1", timeout.Token, body: "put-body").ConfigureAwait(false);
            var patch = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "PATCH", "/items/1", timeout.Token, body: "patch-body").ConfigureAwait(false);
            var delete = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "DELETE", "/items/1", timeout.Token, body: "delete-body").ConfigureAwait(false);
            var upstreamRequests = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            AssertEx.Equal("put", put.Body);
            AssertEx.Equal("patch", patch.Body);
            AssertEx.Equal("delete", delete.Body);
            AssertEx.True(upstreamRequests[0].StartsWith("PUT /items/1 HTTP/1.1", StringComparison.Ordinal), upstreamRequests[0]);
            AssertEx.True(upstreamRequests[0].EndsWith("put-body", StringComparison.Ordinal), upstreamRequests[0]);
            AssertEx.True(upstreamRequests[1].StartsWith("PATCH /items/1 HTTP/1.1", StringComparison.Ordinal), upstreamRequests[1]);
            AssertEx.True(upstreamRequests[1].EndsWith("patch-body", StringComparison.Ordinal), upstreamRequests[1]);
            AssertEx.True(upstreamRequests[2].StartsWith("DELETE /items/1 HTTP/1.1", StringComparison.Ordinal), upstreamRequests[2]);
            AssertEx.True(upstreamRequests[2].EndsWith("delete-body", StringComparison.Ordinal), upstreamRequests[2]);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
            temp.Dispose();
        }
    }

    public static async Task Http3PathRewriteAppliesToProxyRouteAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3ProxyRouteScenarioAsync("GET", "/public/api/users?id=1", "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nok", routeExtraJson: """
                  "pathRewrite": {
                    "stripPrefix": "/public"
                  },
            """).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /api/users?id=1 HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task Http3BodySizeLimitAppliesAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3ProxyRouteScenarioAsync("POST", "/too-large", "", requestBody: "too-large", routeExtraJson: """
                  "overrides": {
                    "maxRequestBodyBytes": 4
                  },
            """).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("413", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("Payload Too Large", result.Body);
        AssertEx.Equal("", result.UpstreamRequest);
        AssertEx.True(result.Metrics.Http3.RejectedRequests.ContainsKey("request_body_too_large"));
    }

    public static void RemovedHttp3BufferedRequestBodyLimitIsRejectedByParser()
    {
        var parser = new SiteConfigurationParser();
        try
        {
            parser.ReadSiteText("""
                {
                  "name": "http3",
                  "listeners": [
                    {
                      "name": "main",
                      "address": "127.0.0.1",
                      "port": 8443,
                      "transport": "https",
                      "protocols": "http3",
                      "http3MaxBufferedRequestBodyBytes": 4,
                      "defaultCertificateId": "home-cert"
                    }
                  ],
                  "host": "localhost"
                }
                """, SiteConfigurationFormat.Json);
        }
        catch (System.Text.Json.JsonException exception)
        {
            AssertEx.True(exception.Message.Contains("http3MaxBufferedRequestBodyBytes", StringComparison.Ordinal), exception.Message);
            return;
        }

        throw new InvalidOperationException("Expected removed HTTP/3 body buffer config to be rejected.");
    }

    public static async Task Http3RequestWithBodyIsNotRetriedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpUdpPort();
        var firstUpstreamPort = GetFreeTcpPort();
        var secondUpstreamPort = GetFreeTcpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3RetrySite(temp.Path, proxyPort, firstUpstreamPort, secondUpstreamPort);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var response = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), "GET", "/retry-body", timeout.Token, body: "not-replayable").ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            var status = HeaderValue(response.Headers, ":status");
            AssertEx.True(status is "502" or "504", status);
            AssertEx.True(metrics.Resilience.RetrySkipped.Any(static skipped => string.Equals(skipped.Reason, "request_body", StringComparison.Ordinal)));
            AssertEx.Equal(0L, metrics.Resilience.RetryAttempts);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
            temp.Dispose();
        }
    }

    public static async Task InvalidFrameSequenceIsRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/sequence", "unused", dataBeforeHeaders: true).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("400", HeaderValue(result.Headers, ":status"));
        AssertEx.True(result.Metrics.Http3.ProtocolErrors.ContainsKey("unexpected_data"));
    }

    public static async Task UnexpectedControlFrameOnRequestStreamIsRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/control", "unused", settingsAfterHeaders: true).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("400", HeaderValue(result.Headers, ":status"));
        AssertEx.True(result.Metrics.Http3.ProtocolErrors.ContainsKey("unexpected_control_frame"));
    }

    public static async Task GoAwayFrameOnRequestStreamIsRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/goaway", "unused", goAwayAfterHeaders: true).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("400", HeaderValue(result.Headers, ":status"));
        AssertEx.True(result.Metrics.Http3.ProtocolErrors.ContainsKey("unexpected_control_frame"));
    }

    public static async Task DuplicateHeadersAfterHeadersIsRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/duplicate", "unused", duplicateHeadersAfterHeaders: true).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("400", HeaderValue(result.Headers, ":status"));
        AssertEx.True(result.Metrics.Http3.ProtocolErrors.ContainsKey("duplicate_headers"));
        AssertEx.Equal(0L, result.Metrics.Http3.ActiveStreams);
    }

    public static async Task UnknownFrameBeforeHeadersIsRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/unknown", "unused", unknownFrameBeforeHeaders: true).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("400", HeaderValue(result.Headers, ":status"));
        AssertEx.True(result.Metrics.Http3.ProtocolErrors.ContainsKey("unsupported_frame"));
        AssertEx.Equal(0L, result.Metrics.Http3.ActiveStreams);
    }

    public static async Task MaxPushFrameOnRequestStreamIsRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteScenarioAsync("GET", "/max-push", "unused", maxPushAfterHeaders: true).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("400", HeaderValue(result.Headers, ":status"));
        AssertEx.True(result.Metrics.Http3.ProtocolErrors.ContainsKey("unexpected_control_frame"));
        AssertEx.Equal(0L, result.Metrics.Http3.ActiveStreams);
    }

    public static async Task StreamLevelProtocolErrorDoesNotPoisonConnectionAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http3", staticBody: "still-open");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
        await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
        var connection = (await ConnectHttp3Async(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), timeout.Token).ConfigureAwait(false));
        await using var connectionDisposal = connection.ConfigureAwait(false);
        {
            var badStream = (await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeout.Token).ConfigureAwait(false));
            await using var badStreamDisposal = badStream.ConfigureAwait(false);
            using var request = new MemoryStream();
            Http3Codec.WriteFrame(request, Http3Codec.DataFrame, ReadOnlySpan<byte>.Empty);
            await badStream.WriteAsync(request.ToArray(), completeWrites: true, timeout.Token).ConfigureAwait(false);
            var badResponse = DecodeHttp3Response(await ReadToEndAsync(badStream, timeout.Token).ConfigureAwait(false));
            AssertEx.Equal("400", HeaderValue(badResponse.Headers, ":status"));
        }

        {
            var goodStream = (await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeout.Token).ConfigureAwait(false));
            await using var goodStreamDisposal = goodStream.ConfigureAwait(false);
            await WriteHttp3RequestAsync(goodStream, "GET", "/ok", null, timeout.Token).ConfigureAwait(false);
            var goodResponse = DecodeHttp3Response(await ReadToEndAsync(goodStream, timeout.Token).ConfigureAwait(false));
            AssertEx.Equal("200", HeaderValue(goodResponse.Headers, ":status"));
            AssertEx.Equal("still-open", goodResponse.Body);
        }

        await connection.CloseAsync(0, CancellationToken.None).ConfigureAwait(false);
    }

    public static async Task ConcurrentStreamResetDoesNotLeakActiveStreamsAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http3", staticBody: "good");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var connection = (await ConnectHttp3Async(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), timeout.Token).ConfigureAwait(false));
            await using var connectionDisposal = connection.ConfigureAwait(false);
            var badStream = (await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeout.Token).ConfigureAwait(false));
            await using var badStreamDisposal = badStream.ConfigureAwait(false);
            var goodStream = (await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeout.Token).ConfigureAwait(false));
            await using var goodStreamDisposal = goodStream.ConfigureAwait(false);
            using var badRequest = new MemoryStream();
            Http3Codec.WriteFrame(badRequest, Http3Codec.DataFrame, ReadOnlySpan<byte>.Empty);
            await badStream.WriteAsync(badRequest.ToArray(), completeWrites: true, timeout.Token).ConfigureAwait(false);
            await WriteHttp3RequestAsync(goodStream, "GET", "/good", null, timeout.Token).ConfigureAwait(false);
            var badResponse = DecodeHttp3Response(await ReadToEndAsync(badStream, timeout.Token).ConfigureAwait(false));
            var goodResponse = DecodeHttp3Response(await ReadToEndAsync(goodStream, timeout.Token).ConfigureAwait(false));
            var metricsStore = host.Services.GetRequiredService<ProxyMetrics>();
            await WaitForHttp3StreamsToDrainAsync(metricsStore, timeout.Token).ConfigureAwait(false);
            var metrics = metricsStore.Snapshot();
            AssertEx.Equal("400", HeaderValue(badResponse.Headers, ":status"));
            AssertEx.Equal("200", HeaderValue(goodResponse.Headers, ":status"));
            AssertEx.Equal("good", goodResponse.Body);
            AssertEx.Equal(0L, metrics.Http3.ActiveStreams);
            await connection.CloseAsync(0, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task QpackDecodeFailureDoesNotReachRouteSelectionAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunHttp3GeneratedRouteRawHeaderBlockScenarioAsync([0, 0, 0x80]).ConfigureAwait(false);
        await using var resultDisposal = result.ConfigureAwait(false);
        AssertEx.Equal("400", HeaderValue(result.Headers, ":status"));
        AssertEx.Equal("Bad Request", result.Body);
        AssertEx.Equal(0L, result.Metrics.Http3.Requests);
        AssertEx.False(result.Metrics.Http3.GeneratedResponses > 0);
        AssertEx.True(result.Metrics.Http3.ProtocolErrors.ContainsKey("unsupported_qpack_index"));
    }

    public static async Task ProtocolErrorBudgetClosesAbusiveConnectionAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        using var temp = TemporaryDirectory.Create();
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http3", staticBody: "budget");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var connection = (await ConnectHttp3Async(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), timeout.Token).ConfigureAwait(false));
            await using var connectionDisposal = connection.ConfigureAwait(false);
            for (var index = 0; index < 8; index++)
            {
                var stream = (await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeout.Token).ConfigureAwait(false));
                await using var streamDisposal = stream.ConfigureAwait(false);
                using var request = new MemoryStream();
                Http3Codec.WriteFrame(request, Http3Codec.DataFrame, ReadOnlySpan<byte>.Empty);
                await stream.WriteAsync(request.ToArray(), completeWrites: true, timeout.Token).ConfigureAwait(false);
                _ = await ReadToEndAsync(stream, timeout.Token).ConfigureAwait(false);
            }

            var metricsStore = host.Services.GetRequiredService<ProxyMetrics>();
            await WaitForHttp3StreamsToDrainAsync(metricsStore, timeout.Token).ConfigureAwait(false);
            var metrics = metricsStore.Snapshot();
            AssertEx.True(metrics.Http3.ProtocolErrors.TryGetValue("unexpected_data", out var errors), "missing unexpected_data metric");
            AssertEx.True(errors >= 8);
            AssertEx.Equal(0L, metrics.Http3.ActiveStreams);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static void OversizedHeaderBlockIsRejected()
    {
        var headerBlock = Http3Codec.EncodeHeaderBlock([new ProxyHeaderField(":method", "GET"), new ProxyHeaderField(":scheme", "https"), new ProxyHeaderField(":authority", "localhost"), new ProxyHeaderField(":path", "/"), new ProxyHeaderField("x-large", new string ('a', 256))]);
        var ok = Http3Codec.TryDecodeHeaderBlock(headerBlock, maxHeaderBytes: 32, out _, out var reason);
        AssertEx.False(ok);
        AssertEx.Equal("header_list_too_large", reason);
    }

    public static void QpackHeaderBlockAtExactLimitIsAccepted()
    {
        var headerBlock = Http3Codec.EncodeHeaderBlock([new ProxyHeaderField(":method", "GET"), new ProxyHeaderField(":scheme", "https"), new ProxyHeaderField(":authority", "localhost"), new ProxyHeaderField(":path", "/boundary"), new ProxyHeaderField("x-boundary", "ok")]);
        var ok = Http3Codec.TryDecodeHeaderBlock(headerBlock, maxHeaderBytes: headerBlock.Length, out var headers, out var reason);
        AssertEx.True(ok, reason);
        AssertEx.Equal("/boundary", headers.Single(static header => string.Equals(header.Name, ":path", StringComparison.Ordinal)).Value);
    }

    public static void UnsupportedQpackDynamicTableUsageIsRejected()
    {
        var block = new byte[]
        {
            0,
            0,
            0x80
        };
        var ok = Http3Codec.TryDecodeHeaderBlock(block, maxHeaderBytes: 32, out _, out var reason);
        AssertEx.False(ok);
        AssertEx.Equal("unsupported_qpack_index", reason);
    }

    public static void InvalidQpackStaticTableReferenceIsRejected()
    {
        var block = new byte[]
        {
            0,
            0,
            0xff,
            0x7f
        };
        var ok = Http3Codec.TryDecodeHeaderBlock(block, maxHeaderBytes: 1024, out _, out var reason);
        AssertEx.False(ok);
        AssertEx.Equal("unsupported_qpack_index", reason);
    }

    public static void UnsupportedQpackDynamicTablePrefixIsRejected()
    {
        var block = new byte[]
        {
            1,
            0
        };
        var ok = Http3Codec.TryDecodeHeaderBlock(block, maxHeaderBytes: 32, out _, out var reason);
        AssertEx.False(ok);
        AssertEx.Equal("unsupported_qpack_dynamic_table", reason);
    }

    public static void QpackHuffmanStaticNameReferenceDecodes()
    {
        var block = new byte[]
        {
            0x00,
            0x00,
            0x50,
            0x8c,
            0xf1,
            0xe3,
            0xc2,
            0xe5,
            0xf2,
            0x3a,
            0x6b,
            0xa0,
            0xab,
            0x90,
            0xf4,
            0xff
        };
        var ok = Http3Codec.TryDecodeHeaderBlock(block, maxHeaderBytes: 1024, out var headers, out var reason);
        AssertEx.True(ok, reason);
        AssertEx.Equal(":authority", headers[0].Name);
        AssertEx.Equal("www.example.com", headers[0].Value);
    }

    public static void MalformedPseudoHeadersAreRejected()
    {
        var headers = new[]
        {
            new ProxyHeaderField(":method", "GET"),
            new ProxyHeaderField(":method", "HEAD"),
            new ProxyHeaderField(":scheme", "https"),
            new ProxyHeaderField(":authority", "localhost"),
            new ProxyHeaderField(":path", "/")
        };
        var reason = RejectHttp3Request(headers);
        AssertEx.Equal("invalid_pseudo_header", reason);
    }

    public static void PseudoHeaderAfterRegularHeaderIsRejected()
    {
        var headers = new[]
        {
            new ProxyHeaderField(":method", "GET"),
            new ProxyHeaderField(":scheme", "https"),
            new ProxyHeaderField("x-before", "1"),
            new ProxyHeaderField(":authority", "localhost"),
            new ProxyHeaderField(":path", "/")
        };
        var reason = RejectHttp3Request(headers);
        AssertEx.Equal("invalid_pseudo_header", reason);
    }

    public static void ForbiddenPseudoHeaderIsRejected()
    {
        var headers = new[]
        {
            new ProxyHeaderField(":method", "GET"),
            new ProxyHeaderField(":scheme", "https"),
            new ProxyHeaderField(":authority", "localhost"),
            new ProxyHeaderField(":path", "/"),
            new ProxyHeaderField(":status", "200")
        };
        var reason = RejectHttp3Request(headers);
        AssertEx.Equal("invalid_pseudo_header", reason);
    }

    public static void MissingPseudoHeadersAreRejected()
    {
        var headers = new[]
        {
            new ProxyHeaderField(":method", "GET"),
            new ProxyHeaderField(":scheme", "https"),
            new ProxyHeaderField(":authority", "localhost")
        };
        var reason = RejectHttp3Request(headers);
        AssertEx.Equal("missing_pseudo_header", reason);
    }

    public static void ForbiddenConnectionHeadersAreRejected()
    {
        var headers = new[]
        {
            new ProxyHeaderField(":method", "GET"),
            new ProxyHeaderField(":scheme", "https"),
            new ProxyHeaderField(":authority", "localhost"),
            new ProxyHeaderField(":path", "/"),
            new ProxyHeaderField("connection", "close")
        };
        var reason = RejectHttp3Request(headers);
        AssertEx.Equal("forbidden_header", reason);
    }

    public static void InvalidRegularHeaderNameIsRejected()
    {
        var headers = new[]
        {
            new ProxyHeaderField(":method", "GET"),
            new ProxyHeaderField(":scheme", "https"),
            new ProxyHeaderField(":authority", "localhost"),
            new ProxyHeaderField(":path", "/"),
            new ProxyHeaderField("bad header", "value")
        };
        var reason = RejectHttp3Request(headers);
        AssertEx.Equal("invalid_header_name", reason);
    }

    public static void InvalidPseudoHeaderValuesAreRejected()
    {
        var headers = new[]
        {
            new ProxyHeaderField(":method", "GE T"),
            new ProxyHeaderField(":scheme", "https"),
            new ProxyHeaderField(":authority", "local/host"),
            new ProxyHeaderField(":path", "/")
        };
        var reason = RejectHttp3Request(headers);
        AssertEx.Equal("invalid_method", reason);
    }

    public static void MalformedAuthorityAndPathAreRejected()
    {
        var badAuthority = new[]
        {
            new ProxyHeaderField(":method", "GET"),
            new ProxyHeaderField(":scheme", "https"),
            new ProxyHeaderField(":authority", "local?host"),
            new ProxyHeaderField(":path", "/")
        };
        var badPath = new[]
        {
            new ProxyHeaderField(":method", "GET"),
            new ProxyHeaderField(":scheme", "https"),
            new ProxyHeaderField(":authority", "localhost"),
            new ProxyHeaderField(":path", "/fragment#bad")
        };
        var authorityReason = RejectHttp3Request(badAuthority);
        var pathReason = RejectHttp3Request(badPath);
        AssertEx.Equal("invalid_target", authorityReason);
        AssertEx.Equal("invalid_target", pathReason);
    }

    public static void ConnectSpecificPseudoHeaderRulesAreEnforced()
    {
        var connectWithPath = new[]
        {
            new ProxyHeaderField(":method", "CONNECT"),
            new ProxyHeaderField(":authority", "upstream.test:443"),
            new ProxyHeaderField(":path", "/")
        };
        var connectWithBody = new[]
        {
            new ProxyHeaderField(":method", "CONNECT"),
            new ProxyHeaderField(":authority", "upstream.test:443"),
            new ProxyHeaderField("content-length", "1")
        };
        var pathReason = RejectHttp3Request(connectWithPath);
        var bodyReason = RejectHttp3Request(connectWithBody);
        AssertEx.Equal("malformed_connect", pathReason);
        AssertEx.Equal("connect_body_unsupported", bodyReason);
    }

    private static string RejectHttp3Request(IReadOnlyList<ProxyHeaderField> headers)
    {
        var result = Http3RequestTranslator.BuildRequest(headers, new Http3RequestTranslationListenerInput(IsHttps: true));
        AssertEx.True(result is Http3RequestTranslationResult.RejectedResult);
        return ((Http3RequestTranslationResult.RejectedResult)result).Reason;
    }

    public static void MetricsIncludeHttp3Counters()
    {
        var metrics = new ProxyMetrics();
        metrics.QuicListenerStarted();
        metrics.Http3ConnectionAccepted();
        metrics.Http3ConnectionClosed();
        metrics.Http3RequestReceived();
        metrics.Http3RequestCompleted("GET", 200, "success");
        metrics.Http3ProxiedRequest();
        metrics.Http3GeneratedResponse();
        metrics.Http3StreamStarted();
        metrics.Http3StreamEnded();
        metrics.Http3StreamReset();
        metrics.Http3StreamedResponse();
        metrics.Http3ResponseStreamStarted();
        metrics.Http3ResponseStreamEnded();
        metrics.AddHttp3ResponseBytesSent(12);
        metrics.AddHttp3RequestBodyBytesReceived(5);
        metrics.Http3ResponseStreamReset();
        metrics.Http3AltSvcEmitted();
        metrics.Http3AltSvcSuppressed();
        metrics.Http3RequestRejected("method_unsupported");
        metrics.Http3ProtocolError("invalid_frame");
        metrics.SetActiveQuicListeners(1);
        var snapshot = metrics.Snapshot();
        VerifyHttp3CounterSnapshot(snapshot);

    }

    private static void VerifyHttp3CounterSnapshot(ProxyMetricsSnapshot snapshot)
    {
        AssertEx.Equal(1L, snapshot.Http3.QuicListenerStartSuccesses);
        AssertEx.Equal(1L, snapshot.Http3.AcceptedConnections);
        AssertEx.Equal(0L, snapshot.Http3.ActiveConnections);
        AssertEx.Equal(1L, snapshot.Http3.Requests);
        #pragma warning disable HLQ005 // Assert exactly one matching outcome; First would accept duplicate metrics and weaken this test.
        AssertEx.Equal(1L, snapshot.Http3.RequestsByOutcome.Single(static item => string.Equals(item.Method, "GET", StringComparison.Ordinal) && string.Equals(item.Outcome, "success", StringComparison.Ordinal) && string.Equals(item.StatusClass, "2xx", StringComparison.Ordinal)).Count);
        #pragma warning restore HLQ005
        AssertEx.Equal(1L, snapshot.Http3.ProxiedRequests);
        AssertEx.Equal(1L, snapshot.Http3.GeneratedResponses);
        AssertEx.Equal(0L, snapshot.Http3.ActiveStreams);
        AssertEx.Equal(1L, snapshot.Http3.StreamResets);
        AssertEx.Equal(1L, snapshot.Http3.StreamedResponses);
        AssertEx.Equal(0L, snapshot.Http3.ActiveResponseStreams);
        AssertEx.Equal(12L, snapshot.Http3.ResponseBytesSent);
        AssertEx.Equal(5L, snapshot.Http3.RequestBodyBytesReceived);
        AssertEx.Equal(1L, snapshot.Http3.ResponseStreamResets);
        AssertEx.Equal(1L, snapshot.Http3.AltSvcEmitted);
        AssertEx.Equal(1L, snapshot.Http3.AltSvcSuppressed);
        AssertEx.Equal(1L, snapshot.Http3.RejectedRequests["method_unsupported"]);
        AssertEx.Equal(1L, snapshot.Http3.ProtocolErrors["invalid_frame"]);
        AssertEx.Equal(1L, snapshot.Http3.ActiveQuicListeners);
    }

    public static void ConfigLintReportsHttp3DefaultReadinessIssues()
    {
        var service = new ConfigLintService(new ProxyConfigLintActiveConfigurationSource(new ProxyConfigurationStore(), TestHttp3PlatformSupport.SupportedSource), new ProxyConfigLintSubmittedConfigurationSource(new SiteConfigurationParser(), TestHttp3PlatformSupport.SupportedSource, new ProxyEndpointAddressPolicy(), new ProxyUrlSyntaxPolicy()), new ProxyConfigLintRuntimeStateSource(new ProxyRuntimeState(TimeProvider.System)), new ProxyMetrics(), new ProxyConfigLintSourceNameFormatter(), new ProxyAdminUrlPolicy(), TimeProvider.System);
        var result = service.LintSubmitted(new ConfigLintRequest("json", """
            {
              "name": "http3",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": 8443,
                  "transport": "https",
                  "protocols": "http3",
                  "http3Enablement": "default",
                  "defaultCertificateId": "home-cert"
                }
              ],
              "host": "localhost",
              "routes": [
                {
                  "name": "static",
                  "pathPrefix": "/",
                  "action": "staticResponse",
                  "staticResponse": {
                    "statusCode": 200,
                    "contentType": "text/plain",
                    "body": "ok"
                  }
                }
              ]
            }
            """));
        var codes = result.Findings.Select(static finding => finding.Code).ToArray();
        AssertEx.True(codes.Contains("http3_alt_svc_not_ready"));
        AssertEx.False(codes.Any(static code => code.Contains("buffer", StringComparison.OrdinalIgnoreCase)));
        AssertEx.False(codes.Contains("http3_default_readiness_buffered_body"));
        AssertEx.False(codes.Contains("http3_default_readiness_qpack_static_only"));
    }

    private static async Task<Http3ScenarioResult> RunHttp3GeneratedRouteScenarioAsync(string method, string target, string body, bool includeBodyData = false, bool dataBeforeHeaders = false, bool settingsAfterHeaders = false, bool goAwayAfterHeaders = false, bool duplicateHeadersAfterHeaders = false, bool unknownFrameBeforeHeaders = false, bool maxPushAfterHeaders = false, string? routeJson = null)
    {
        var scenario = new UntransferredHttp3Scenario();
        await using var scenarioLifetime = scenario.ConfigureAwait(false);
        var temp = scenario.Directory;
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http3", body, routeJson: routeJson);
        var host = scenario.CreateHost();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
        await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
        var response = await SendHttp3RequestAsync(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), method, target, timeout.Token, includeBodyData: includeBodyData, dataBeforeHeaders: dataBeforeHeaders, settingsAfterHeaders: settingsAfterHeaders, goAwayAfterHeaders: goAwayAfterHeaders, duplicateHeadersAfterHeaders: duplicateHeadersAfterHeaders, unknownFrameBeforeHeaders: unknownFrameBeforeHeaders, maxPushAfterHeaders: maxPushAfterHeaders).ConfigureAwait(false);
        var metricsStore = host.Services.GetRequiredService<ProxyMetrics>();
        await WaitForHttp3StreamsToDrainAsync(metricsStore, timeout.Token).ConfigureAwait(false);
        var metrics = metricsStore.Snapshot();
        return scenario.Transfer(response.Headers, response.Body, metrics, "");
    }

    private static async Task<Http3ScenarioResult> RunHttp3GeneratedRouteRawHeaderBlockScenarioAsync(byte[] headerBlock)
    {
        var scenario = new UntransferredHttp3Scenario();
        await using var scenarioLifetime = scenario.ConfigureAwait(false);
        var temp = scenario.Directory;
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http3", "unused");
        var host = scenario.CreateHost();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
        await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
        var response = await SendHttp3RawHeaderBlockAsync(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), headerBlock, timeout.Token).ConfigureAwait(false);
        var metricsStore = host.Services.GetRequiredService<ProxyMetrics>();
        await WaitForHttp3StreamsToDrainAsync(metricsStore, timeout.Token).ConfigureAwait(false);
        var metrics = metricsStore.Snapshot();
        return scenario.Transfer(response.Headers, response.Body, metrics, "");
    }

    private static async Task<Http3ScenarioResult> RunHttp3GeneratedRouteRawHeadersScenarioAsync(IReadOnlyList<ProxyHeaderField> headers)
    {
        var scenario = new UntransferredHttp3Scenario();
        await using var scenarioLifetime = scenario.ConfigureAwait(false);
        var temp = scenario.Directory;
        var port = GetFreeTcpUdpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3Site(temp.Path, port, "http3", "unused");
        var host = scenario.CreateHost();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
        await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
        var response = await SendHttp3RequestAsync(port, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), headers, timeout.Token).ConfigureAwait(false);
        var metricsStore = host.Services.GetRequiredService<ProxyMetrics>();
        await WaitForHttp3StreamsToDrainAsync(metricsStore, timeout.Token).ConfigureAwait(false);
        var metrics = metricsStore.Snapshot();
        return scenario.Transfer(response.Headers, response.Body, metrics, "");
    }

    private static async Task<Http3ScenarioResult> RunHttp3ProxyRouteScenarioAsync(string method, string target, string upstreamResponse, string? requestBody = null, string routeExtraJson = "", string listenerExtraJson = "")
    {
        var scenario = new UntransferredHttp3Scenario();
        await using var scenarioLifetime = scenario.ConfigureAwait(false);
        var temp = scenario.Directory;
        var proxyPort = GetFreeTcpUdpPort();
        var upstreamPort = GetFreeTcpPort();
        WriteCertificateConfig(temp.Path);
        WriteHttp3ProxySite(temp.Path, proxyPort, upstreamPort, routeExtraJson, listenerExtraJson);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = string.IsNullOrEmpty(upstreamResponse) ? Task.FromResult("") : RunSingleResponseUpstreamAsync(upstreamPort, upstreamResponse, timeout.Token);
        try
        {
            var host = scenario.CreateHost();
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "main", "quic", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var response = await SendHttp3RequestAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(temp.Path, "certs", "home.pfx"), "secret"), method, target, timeout.Token, body: requestBody).ConfigureAwait(false);
            var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metricsStore = host.Services.GetRequiredService<ProxyMetrics>();
            await WaitForHttp3StreamsToDrainAsync(metricsStore, timeout.Token).ConfigureAwait(false);
            var metrics = metricsStore.Snapshot();
            return scenario.Transfer(response.Headers, response.Body, metrics, upstreamRequest);
        }
        finally
        {
            try { if (!upstreamTask.IsCompleted) await timeout.CancelAsync().ConfigureAwait(false); }
            finally
            {
                try { await upstreamTask.ConfigureAwait(false); }
                catch (Exception exception) when (timeout.IsCancellationRequested && exception is OperationCanceledException or IOException or SocketException) { }
            }
        }
    }

    private static async Task<Http3Response> SendHttp3RequestAsync(int port, RemoteCertificateValidationCallback certificateValidation, string method, string target, CancellationToken cancellationToken, bool includeBodyData = false, bool dataBeforeHeaders = false, string? body = null, bool settingsAfterHeaders = false, bool goAwayAfterHeaders = false, bool duplicateHeadersAfterHeaders = false, bool unknownFrameBeforeHeaders = false, bool maxPushAfterHeaders = false, Action<string>? certificateSubjectObserver = null)
    {
        var connection = (await ConnectHttp3Async(port, certificateValidation, cancellationToken, certificateSubjectObserver).ConfigureAwait(false));
        await using var connectionDisposal = connection.ConfigureAwait(false);
        var stream = (await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).ConfigureAwait(false));
        await using var streamDisposal = stream.ConfigureAwait(false);
        await WriteHttp3RequestAsync(stream, method, target, body ?? (includeBodyData ? "body" : null), cancellationToken, dataBeforeHeaders, settingsAfterHeaders, goAwayAfterHeaders, duplicateHeadersAfterHeaders, unknownFrameBeforeHeaders, maxPushAfterHeaders).ConfigureAwait(false);
        var responseBytes = await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
        var response = DecodeHttp3Response(responseBytes);
        await connection.CloseAsync(0, CancellationToken.None).ConfigureAwait(false);
        return response;
    }

    private static async Task<Http3Response> SendHttp3RequestAsync(int port, RemoteCertificateValidationCallback certificateValidation, IReadOnlyList<ProxyHeaderField> headers, CancellationToken cancellationToken)
    {
        var connection = (await ConnectHttp3Async(port, certificateValidation, cancellationToken).ConfigureAwait(false));
        await using var connectionDisposal = connection.ConfigureAwait(false);
        var stream = (await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).ConfigureAwait(false));
        await using var streamDisposal = stream.ConfigureAwait(false);
        await WriteHttp3RequestAsync(stream, headers, body: null, cancellationToken).ConfigureAwait(false);
        var responseBytes = await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
        var response = DecodeHttp3Response(responseBytes);
        await connection.CloseAsync(0, CancellationToken.None).ConfigureAwait(false);
        return response;
    }

    private static async Task<Http3Response> SendHttp3RawHeaderBlockAsync(int port, RemoteCertificateValidationCallback certificateValidation, byte[] headerBlock, CancellationToken cancellationToken)
    {
        var connection = (await ConnectHttp3Async(port, certificateValidation, cancellationToken).ConfigureAwait(false));
        await using var connectionDisposal = connection.ConfigureAwait(false);
        var stream = (await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).ConfigureAwait(false));
        await using var streamDisposal = stream.ConfigureAwait(false);
        using var request = new MemoryStream();
        Http3Codec.WriteFrame(request, Http3Codec.HeadersFrame, headerBlock);
        await stream.WriteAsync(request.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
        var responseBytes = await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
        var response = DecodeHttp3Response(responseBytes);
        await connection.CloseAsync(0, CancellationToken.None).ConfigureAwait(false);
        return response;
    }

    private static async ValueTask WriteHttp3RequestAsync(QuicStream stream, string method, string target, string? body, CancellationToken cancellationToken, bool dataBeforeHeaders = false, bool settingsAfterHeaders = false, bool goAwayAfterHeaders = false, bool duplicateHeadersAfterHeaders = false, bool unknownFrameBeforeHeaders = false, bool maxPushAfterHeaders = false)
    {
        List<ProxyHeaderField> requestHeaders = [new ProxyHeaderField(":method", method), new ProxyHeaderField(":scheme", "https"), new ProxyHeaderField(":authority", "localhost"), new ProxyHeaderField(":path", target)];
        if (body is not null)
        {
            requestHeaders.Add(new ProxyHeaderField("content-length", Encoding.UTF8.GetByteCount(body).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        await WriteHttp3RequestAsync(stream, requestHeaders, body, cancellationToken, dataBeforeHeaders, settingsAfterHeaders, goAwayAfterHeaders, duplicateHeadersAfterHeaders, unknownFrameBeforeHeaders, maxPushAfterHeaders).ConfigureAwait(false);
    }

    private static async ValueTask WriteHttp3RequestAsync(QuicStream stream, IReadOnlyList<ProxyHeaderField> requestHeaders, string? body, CancellationToken cancellationToken, bool dataBeforeHeaders = false, bool settingsAfterHeaders = false, bool goAwayAfterHeaders = false, bool duplicateHeadersAfterHeaders = false, bool unknownFrameBeforeHeaders = false, bool maxPushAfterHeaders = false)
    {
        var headerBlock = Http3Codec.EncodeHeaderBlock(requestHeaders);
        using var request = new MemoryStream();
        if (unknownFrameBeforeHeaders)
        {
            Http3Codec.WriteFrame(request, 0x21, ReadOnlySpan<byte>.Empty);
        }

        if (dataBeforeHeaders)
        {
            Http3Codec.WriteFrame(request, Http3Codec.DataFrame, ReadOnlySpan<byte>.Empty);
        }

        Http3Codec.WriteFrame(request, Http3Codec.HeadersFrame, headerBlock);
        if (duplicateHeadersAfterHeaders)
        {
            Http3Codec.WriteFrame(request, Http3Codec.HeadersFrame, headerBlock);
        }

        if (settingsAfterHeaders)
        {
            Http3Codec.WriteFrame(request, Http3Codec.SettingsFrame, ReadOnlySpan<byte>.Empty);
        }

        if (goAwayAfterHeaders)
        {
            using var goAwayPayload = new MemoryStream();
            Http3Codec.WriteVarInt(goAwayPayload, 0);
            Http3Codec.WriteFrame(request, Http3Codec.GoAwayFrame, goAwayPayload.ToArray());
        }

        if (maxPushAfterHeaders)
        {
            Http3Codec.WriteFrame(request, 0xD, ReadOnlySpan<byte>.Empty);
        }

        if (body is not null)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            Http3Codec.WriteFrame(request, Http3Codec.DataFrame, bodyBytes);
        }

        await stream.WriteAsync(request.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }

    private static Http3Response DecodeHttp3Response(byte[] responseBytes)
    {
        var offset = 0;
        IReadOnlyList<ProxyHeaderField> headers = [];
        var responseBody = "";
        while (offset < responseBytes.Length)
        {
            if (!Http3Codec.TryReadFrame(responseBytes, ref offset, out var type, out var payload))
            {
                break;
            }

            if (type == Http3Codec.HeadersFrame)
            {
                AssertEx.True(Http3Codec.TryDecodeHeaderBlock(payload.Span, 32 * 1024, out headers, out var reason), reason);
            }
            else if (type == Http3Codec.DataFrame)
            {
                responseBody += Encoding.UTF8.GetString(payload.Span);
            }
        }

        return new Http3Response(headers, responseBody);
    }

    private static async Task<string> ReadFirstHttp3DataAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[256];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return "";
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            var bytes = memory.ToArray();
            var offset = 0;
            while (offset < bytes.Length)
            {
                if (!Http3Codec.TryReadFrame(bytes, ref offset, out var type, out var payload))
                {
                    break;
                }

                if (type == Http3Codec.DataFrame && payload.Length > 0)
                {
                    return Encoding.UTF8.GetString(payload.Span);
                }
            }
        }
    }

    private static async Task<Http3Response> ReadHttp3ResponseRemainderAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        return DecodeHttp3Response(await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static ValueTask<QuicConnection> ConnectHttp3Async(int port, RemoteCertificateValidationCallback certificateValidation, CancellationToken cancellationToken, Action<string>? certificateSubjectObserver = null)
    {
        return QuicConnection.ConnectAsync(new QuicClientConnectionOptions { RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, port), ClientAuthenticationOptions = new SslClientAuthenticationOptions { TargetHost = "localhost", ApplicationProtocols = [Http3Alpn], RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
        {
            if (!certificateValidation(sender, certificate, chain, errors)) return false;
            if (certificate is not null && certificateSubjectObserver is not null)
            {
                certificateSubjectObserver(certificate.Subject);
            }

            return true;
        } }, MaxInboundBidirectionalStreams = 4, MaxInboundUnidirectionalStreams = 4, IdleTimeout = TimeSpan.FromSeconds(5), HandshakeTimeout = TimeSpan.FromSeconds(5), DefaultCloseErrorCode = 0x100, DefaultStreamErrorCode = 0x100 }, cancellationToken);
    }

    private static async Task<string> SendHttp1TlsRequestAsync(int port, RemoteCertificateValidationCallback certificateValidation, string target, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
        var tls = new SslStream(client.GetStream(), false, certificateValidation);
        await using var tlsDisposal = tls.ConfigureAwait(false);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = SslProtocols.None, ApplicationProtocols = [SslApplicationProtocol.Http11] }, cancellationToken).ConfigureAwait(false);
        await tls.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"), cancellationToken).ConfigureAwait(false);
        return Encoding.ASCII.GetString(await ReadToEndAsync(tls, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<string> RunSingleResponseUpstreamAsync(int upstreamPort, string response, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await using var streamDisposal = stream.ConfigureAwait(false);
            var request = await ReadHttp1RequestAsync(stream, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
            return request;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string[]> RunSequentialResponseUpstreamAsync(int upstreamPort, IReadOnlyList<string> responses, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        List<string> requests = [];
        try
        {
            foreach (var response in responses)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                requests.Add(await ReadHttp1RequestAsync(stream, cancellationToken).ConfigureAwait(false));
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
            }

            return requests.ToArray();
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> RunStreamingResponseUpstreamAsync(int upstreamPort, TaskCompletionSource firstChunkSent, TaskCompletionSource releaseUpstream, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await using var streamDisposal = stream.ConfigureAwait(false);
            var request = await ReadHttp1RequestAsync(stream, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 13\r\n\r\nstream-"), cancellationToken).ConfigureAwait(false);
            firstChunkSent.SetResult();
            await releaseUpstream.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("second"), cancellationToken).ConfigureAwait(false);
            return request;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> ReadHttp1RequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[256];
        var headerEnd = -1;
        var contentLength = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return Encoding.ASCII.GetString(memory.ToArray());
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            var bytes = memory.ToArray();
            headerEnd = headerEnd < 0 ? IndexOfHeaderEnd(bytes) : headerEnd;
            if (headerEnd >= 0)
            {
                contentLength = contentLength == 0 ? ParseContentLength(bytes, headerEnd) : contentLength;
                if (bytes.Length >= headerEnd + 4 + contentLength)
                {
                    return Encoding.ASCII.GetString(bytes);
                }
            }
        }
    }

    private static int IndexOfHeaderEnd(ReadOnlySpan<byte> bytes)
    {
        for (var index = 3; index < bytes.Length; index++)
        {
            if (bytes[index - 3] == (byte)'\r' && bytes[index - 2] == (byte)'\n' && bytes[index - 1] == (byte)'\r' && bytes[index] == (byte)'\n')
            {
                return index - 3;
            }
        }

        return -1;
    }

    private static int ParseContentLength(byte[] bytes, int headerEnd)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, headerEnd);
        foreach (var line in head.Split("\r\n", StringSplitOptions.None))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || !line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return int.TryParse(line[(colon + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 0;
        }

        return 0;
    }

    private static async Task<byte[]> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return memory.ToArray();
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static IHost BuildProxyHost(string dataDirectory, Action<IServiceCollection>? configureServices = null)
    {
        return Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [$"{MdravaDataDirectoryOptions.SectionName}:DataDirectory"] = dataDirectory });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) =>
        {
            services.AddProxyDataPlane(context.Configuration);
            configureServices?.Invoke(services);
        }).Build();
    }

    private static RuntimeListener TestHttp3Listener(string protocols, RuntimeHttp3AltSvcOptions? http3AltSvc = null)
    {
        return new RuntimeListener("main", "127.0.0.1", 8443, true, RuntimeListenerTransport.Https, "default", [], 512, 32 * 1024, 32 * 1024, 1024, 64 * 1024, protocols switch
        {
            var value when string.Equals(value, "http1andhttp2andhttp3", StringComparison.OrdinalIgnoreCase) => RuntimeListenerProtocols.Http1AndHttp2AndHttp3,
            var value when string.Equals(value, "http1andhttp3", StringComparison.OrdinalIgnoreCase) => RuntimeListenerProtocols.Http1AndHttp3,
            var value when string.Equals(value, "http2andhttp3", StringComparison.OrdinalIgnoreCase) => RuntimeListenerProtocols.Http2AndHttp3,
            _ => RuntimeListenerProtocols.Http3
        }, RuntimeHttp3Enablement.Default, http3AltSvc ?? RuntimeHttp3AltSvcOptions.Disabled, RuntimeHttp2Limits.Default);
    }

    private static ProxyListenerStatus ActiveQuicListenerStatus(RuntimeListener listener)
    {
        var quicIdentity = AssertEx.NotNull(listener.QuicIdentity);
        return new ProxyListenerStatus(listener.Name, quicIdentity.Key, quicIdentity.BindKey, "quic", listener.Address, listener.Port, "udp", true, "http3", new ProxyListenerHttp3Status(Configured: true, DefaultEnabled: true, EnablementLevel: "default", EnabledForTraffic: true, DisabledReason: "default_enabled", AltSvcConfigured: true, AltSvcMaxAgeSeconds: listener.Http3AltSvc.MaxAgeSeconds, UdpQuicListenerIdentityModeled: true, QuicIdentity: new ProxyQuicListenerIdentity(listener.Name, listener.Address, listener.Port, TlsEnabled: true)), listener.Http2Limits.MaxConcurrentStreams, listener.Http2Limits.MaxHeaderListBytes, listener.Http2Limits.MaxFrameSize, ProxyListenerState.Active, ActiveConnections: 0, StartedAtUtc: DateTimeOffset.UnixEpoch, StoppedAtUtc: null, LastError: null);
    }

    private sealed class FixedHttp3AltSvcRuntimeListenerSource : IHttp3AltSvcRuntimeListenerSource
    {
        private readonly IReadOnlyList<ProxyListenerStatus> _listeners;
        public FixedHttp3AltSvcRuntimeListenerSource(IReadOnlyList<ProxyListenerStatus> listeners)
        {
            _listeners = listeners;
        }

        public int ReadCount { get; private set; }

        public IReadOnlyList<ProxyListenerStatus> ReadRuntimeListeners()
        {
            ReadCount++;
            return _listeners;
        }
    }

    private static void WriteCertificateConfig(string dataDirectory)
    {
        var certificatePath = Path.Combine(dataDirectory, "certs", "home.pfx");
        TestCertificates.WriteSelfSignedPfx(certificatePath, "localhost", "secret");
        ConfigurationTests.WriteOperationalConfig(dataDirectory, certificateId: "home-cert", certificatePath: "certs/home.pfx", certificatePassword: "secret");
    }

    private static void WriteHttp3Site(string dataDirectory, int port, string protocols, string staticBody, bool altSvcEnabled = false, int altSvcMaxAgeSeconds = 86400, string? http3EnablementOverride = null, string? routeJson = null)
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        var routes = routeJson ?? $$"""
                {
                  "name": "static",
                  "pathPrefix": "/",
                  "action": "staticResponse",
                  "staticResponse": {
                    "statusCode": 200,
                    "contentType": "text/plain; charset=utf-8",
                    "body": "{{staticBody}}"
                  }
                }
            """;
        var http3Enablement = http3EnablementOverride ?? (protocols.Contains("http3", StringComparison.OrdinalIgnoreCase) ? "default" : "disabled");
        File.WriteAllText(Path.Combine(sites, "http3.json"), $$"""
            {
              "name": "http3",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{port}},
                  "transport": "https",
                  "protocols": "{{protocols}}",
                  "http3Enablement": "{{http3Enablement}}",
                  "http3AltSvcEnabled": {{(altSvcEnabled ? "true" : "false")}},
                  "http3AltSvcMaxAgeSeconds": {{altSvcMaxAgeSeconds}},
                  "defaultCertificateId": "home-cert"
                }
              ],
              "host": "localhost",
              "routes": [
            {{routes}}
              ]
            }
            """);
    }

    private static void WriteHttp3ProxySite(string dataDirectory, int proxyPort, int upstreamPort, string routeExtraJson = "", string listenerExtraJson = "")
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        File.WriteAllText(Path.Combine(sites, "http3.json"), $$"""
            {
              "name": "http3",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "protocols": "http3",
                  "http3Enablement": "default",
            {{listenerExtraJson}}
                  "defaultCertificateId": "home-cert"
                }
              ],
              "host": "localhost",
              "routes": [
                {
                  "name": "proxy",
                  "pathPrefix": "/",
                  "action": "proxy",
            {{routeExtraJson}}
                  "upstreams": [
                    {
                      "name": "local-test",
                      "address": "127.0.0.1",
                      "port": {{upstreamPort}}
                    }
                  ]
                }
              ]
            }
            """);
    }

    private static void WriteHttp3RetrySite(string dataDirectory, int proxyPort, int firstUpstreamPort, int secondUpstreamPort)
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        File.WriteAllText(Path.Combine(sites, "http3.json"), $$"""
            {
              "name": "http3",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "protocols": "http3",
                  "http3Enablement": "default",
                  "defaultCertificateId": "home-cert"
                }
              ],
              "host": "localhost",
              "routes": [
                {
                  "name": "proxy",
                  "pathPrefix": "/",
                  "action": "proxy",
                  "retry": {
                    "enabled": true,
                    "maxAttempts": 2,
                    "retryOnConnectFailure": true,
                    "retryMethods": [ "GET", "HEAD" ],
                    "retryBackoffMilliseconds": 0
                  },
                  "upstreams": [
                    {
                      "name": "closed",
                      "address": "127.0.0.1",
                      "port": {{firstUpstreamPort}}
                    },
                    {
                      "name": "local-test",
                      "address": "127.0.0.1",
                      "port": {{secondUpstreamPort}}
                    }
                  ]
                }
              ]
            }
            """);
    }

    private static Task<ProxyListenerStatus> WaitForListenerAsync(ProxyRuntimeState runtimeState, string name, string kind, ProxyListenerState state, CancellationToken cancellationToken)
    {
        return TestWaiters.WaitForListenerAsync(runtimeState, name, kind, state, cancellationToken);
    }

    private static Task WaitForSuccessfulHttp3RequestCompletionAsync(ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        return TestWaiters.UntilAsync(() => metrics.Snapshot().Http3.RequestsByOutcome.Any(static outcome =>
            string.Equals(outcome.Method, "GET", StringComparison.Ordinal) &&
            string.Equals(outcome.StatusClass, "2xx", StringComparison.Ordinal) &&
            string.Equals(outcome.Outcome, "success", StringComparison.Ordinal) && outcome.Count > 0),
            static () => "Timed out waiting for the isolated successful HTTP/3 GET request to complete.", cancellationToken);
    }

    private static Task WaitForHttp3StreamsToDrainAsync(ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        return TestWaiters.WaitForHttp3StreamsToDrainAsync(metrics, cancellationToken);
    }

    private static Task WaitForNoListenerAsync(ProxyRuntimeState runtimeState, string name, string kind, CancellationToken cancellationToken)
    {
        return TestWaiters.WaitForNoListenerAsync(runtimeState, name, kind, cancellationToken);
    }

    private static int GetFreeTcpUdpPort() => TestPortAllocator.GetFreeTcpUdpPort();
    private static int GetFreeTcpPort() => TestPortAllocator.GetFreeTcpPort();
    private static string HeaderValue(IReadOnlyList<ProxyHeaderField> headers, string name)
    {
        return headers.First(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static bool HeaderExists(IReadOnlyList<ProxyHeaderField> headers, string name)
    {
        return headers.Any(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FailingQuicListenerFactory : IHttp3QuicListenerFactory
    {
        public bool IsSupported => true;

        public ValueTask<QuicListener> ListenAsync(RuntimeListener listener, ProxyConfigurationSnapshot snapshot, CancellationToken cancellationToken)
        {
            _ = listener;
            _ = snapshot;
            _ = cancellationToken;
            throw new InvalidOperationException("fake_quic_bind_failure");
        }
    }

    private sealed record Http3Response(IReadOnlyList<ProxyHeaderField> Headers, string Body);
    private sealed class UntransferredHttp3Scenario : IAsyncDisposable
    {
        private TemporaryDirectory? _directory = TemporaryDirectory.Create();
        private IHost? _host;

        public TemporaryDirectory Directory => _directory ?? throw new InvalidOperationException("Scenario ownership was already transferred.");

        public IHost CreateHost() => _host = BuildProxyHost(Directory.Path);

        public Http3ScenarioResult Transfer(IReadOnlyList<ProxyHeaderField> headers, string body, ProxyMetricsSnapshot metrics, string upstreamRequest)
        {
            var result = new Http3ScenarioResult(Directory, _host ?? throw new InvalidOperationException("Scenario host has not been created."), headers, body, metrics, upstreamRequest);
            _directory = null;
            _host = null;
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_host is not null)
                {
                    try { await _host.StopAsync(CancellationToken.None).ConfigureAwait(false); }
                    finally
                    {
                        if (_host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync().ConfigureAwait(false);
                        else _host.Dispose();
                    }
                }
            }
            finally { _directory?.Dispose(); }
        }
    }

    private sealed class Http3ScenarioResult : IAsyncDisposable
    {
        private readonly TemporaryDirectory _directory;
        private readonly IHost _host;
        private readonly Lock _disposeGate = new();
        private Task? _disposeTask;
        public Http3ScenarioResult(TemporaryDirectory directory, IHost host, IReadOnlyList<ProxyHeaderField> headers, string body, ProxyMetricsSnapshot metrics, string upstreamRequest)
        {
            _directory = directory;
            _host = host;
            Headers = headers;
            Body = body;
            Metrics = metrics;
            UpstreamRequest = upstreamRequest;
        }

        public IReadOnlyList<ProxyHeaderField> Headers { get; }
        public string Body { get; }
        public ProxyMetricsSnapshot Metrics { get; }
        public string UpstreamRequest { get; }

        public ValueTask DisposeAsync()
        {
            lock (_disposeGate)
            {
                return new ValueTask(_disposeTask ??= DisposeOwnedAsync());
            }
        }

        private async Task DisposeOwnedAsync()
        {
            try
            {
                try
                {
                    await _host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    if (_host is IAsyncDisposable asyncHost)
                    {
                        await asyncHost.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        _host.Dispose();
                    }
                }
            }
            finally
            {
                _directory.Dispose();
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-h3-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
