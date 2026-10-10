using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ClientHttp2Tests
{
    public static async Task ExistingHttp1BehaviorRemainsUnchangedAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = CreateDataDirectory();
        try
        {
            WriteHttpsProxySite(dataDirectory, proxyPort, upstreamPort, listenerProtocols: "http1AndHttp2");
            WriteCertificateConfig(dataDirectory);
            var upstreamTask = RunSingleResponseUpstreamAsync(upstreamPort, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\n\r\nproxied", timeout.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
                var tls = new SslStream(client.GetStream(), false, TestCertificates.PinServerCertificate(Path.Combine(dataDirectory, "certs", "home.pfx")));
                await using var tlsDisposal = tls.ConfigureAwait(false);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "home.test", EnabledSslProtocols = SslProtocols.None, ApplicationProtocols = [SslApplicationProtocol.Http11] }, timeout.Token).ConfigureAwait(false);
                AssertEx.Equal(SslApplicationProtocol.Http11, tls.NegotiatedApplicationProtocol);
                await tls.WriteAsync(Encoding.ASCII.GetBytes("GET /http1 HTTP/1.1\r\nHost: home.test\r\nConnection: close\r\n\r\n"), timeout.Token).ConfigureAwait(false);
                var response = await ReadToEndAsync(tls, timeout.Token).ConfigureAwait(false);
                var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
                AssertEx.True(upstreamRequest.StartsWith("GET /http1 HTTP/1.1", StringComparison.Ordinal), upstreamRequest);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static void PlaintextHttp2ListenerIsRejected()
    {
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, new ProxyOptions { Listeners = [new ListenerOptions { Name = "bad-h2c", Address = "127.0.0.1", Port = 8080, Transport = "http", Protocols = "http2" }], Routes = [new ProxyRouteOptions { Name = "proxy", Host = "*", PathPrefix = "/", Upstreams = [new UpstreamOptions { Name = "local", Address = "127.0.0.1", Port = 5000 }] }] });
        AssertEx.True(validation.Failed);
        AssertEx.True(AssertEx.NotNull(validation.Failures).Any(static failure => failure.Contains("HTTP/2 requires an HTTPS listener", StringComparison.Ordinal)));
    }

    public static async Task AlpnSelectsHttp2WhenEnabledAsync()
    {
        var result = await RunHttp2ScenarioAsync(SiteWithStaticRoute, request => request.Authority = "home.test").ConfigureAwait(false);
        AssertEx.Equal(SslApplicationProtocol.Http2, result.NegotiatedProtocol);
        AssertEx.Equal(203, result.Response.StatusCode);
    }

    public static async Task Http2RequestMapsToRouteMatcherAsync()
    {
        var result = await RunHttp2ScenarioAsync((dataDirectory, proxyPort, upstreamPort) => WriteHttpsProxySite(dataDirectory, proxyPort, upstreamPort), request =>
        {
            request.Path = "/h2";
            request.Authority = "home.test";
        }, upstreamResponse: "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\n\r\nproxied").ConfigureAwait(false);
        AssertEx.Equal(200, result.Response.StatusCode);
        AssertEx.Equal("proxied", result.Response.BodyText);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /h2 HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task AuthorityMapsToHostRoutingAsync()
    {
        var result = await RunHttp2ScenarioAsync((dataDirectory, proxyPort, upstreamPort) => WriteHttpsProxySite(dataDirectory, proxyPort, upstreamPort, host: "authority.test"), request =>
        {
            request.Authority = "authority.test";
            request.Path = "/authority";
        }, upstreamResponse: "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nok").ConfigureAwait(false);
        AssertEx.Equal(200, result.Response.StatusCode);
        AssertEx.True(result.UpstreamRequest.Contains("Host: authority.test", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
    }

    public static async Task QueryStringIsPreservedAsync()
    {
        var result = await RunHttp2ScenarioAsync((dataDirectory, proxyPort, upstreamPort) => WriteHttpsProxySite(dataDirectory, proxyPort, upstreamPort), request =>
        {
            request.Path = "/search?q=one&sort=two";
            request.Authority = "home.test";
        }, upstreamResponse: "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nok").ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /search?q=one&sort=two HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task InvalidPseudoHeadersAreRejectedAsync()
    {
        var result = await RunHttp2ScenarioAsync(SiteWithStaticRoute, request =>
        {
            request.Authority = "home.test";
            request.DuplicatePathPseudoHeader = true;
        }).ConfigureAwait(false);
        AssertEx.Equal(400, result.Response.StatusCode);
        AssertEx.Equal(1L, result.Metrics.Http2.ProtocolErrors["invalid_pseudo_header"]);
    }

    public static async Task ForbiddenConnectionHeadersAreRejectedAsync()
    {
        var result = await RunHttp2ScenarioAsync(SiteWithStaticRoute, request =>
        {
            request.Authority = "home.test";
            request.Headers.Add(("connection", "close"));
        }).ConfigureAwait(false);
        AssertEx.Equal(400, result.Response.StatusCode);
        AssertEx.Equal(1L, result.Metrics.Http2.ProtocolErrors["forbidden_header"]);
    }

    public static async Task HuffmanRequestHeaderValuesAreDecodedAsync()
    {
        var result = await RunHttp2ScenarioAsync((dataDirectory, proxyPort, upstreamPort) => WriteHttpsProxySite(dataDirectory, proxyPort, upstreamPort), request =>
        {
            request.Authority = "home.test";
            request.Path = "/huffman";
            request.HuffmanValueHeaders.Add(("x-huffman-hpack", "mdrava"));
        }, upstreamResponse: "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nok").ConfigureAwait(false);
        AssertEx.Equal(200, result.Response.StatusCode);
        AssertEx.True(result.UpstreamRequest.Contains("x-huffman-hpack: mdrava", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
    }

    public static async Task ResponseOmitsHopByHopHeadersAsync()
    {
        var result = await RunHttp2ScenarioAsync((dataDirectory, proxyPort, upstreamPort) => WriteHttpsProxySite(dataDirectory, proxyPort, upstreamPort), request =>
        {
            request.Authority = "home.test";
            request.Path = "/headers";
        }, upstreamResponse: "HTTP/1.1 200 OK\r\nConnection: close\r\nKeep-Alive: timeout=5\r\nContent-Length: 2\r\n\r\nok").ConfigureAwait(false);
        AssertEx.Equal(200, result.Response.StatusCode);
        AssertEx.False(result.Response.Headers.ContainsKey("connection"));
        AssertEx.False(result.Response.Headers.ContainsKey("keep-alive"));
    }

    public static async Task StaticResponseRouteWorksOverHttp2Async()
    {
        var result = await RunHttp2ScenarioAsync(SiteWithStaticRoute, request =>
        {
            request.Authority = "home.test";
            request.Path = "/static";
        }).ConfigureAwait(false);
        AssertEx.Equal(203, result.Response.StatusCode);
        AssertEx.Equal("static-h2", result.Response.BodyText);
    }

    public static async Task ActiveHttp2TrafficSurvivesCertificateReloadAndNewConnectionsUseReloadedCertificateAsync()
    {
        var dataDirectory = CreateDataDirectory();
        var proxyPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            WriteCertificateConfig(dataDirectory);
            SiteWithStaticRoute(dataDirectory, proxyPort, 0);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var activeClient = (await Http2TestClient.ConnectAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(dataDirectory, "certs", "home.pfx")), timeout.Token).ConfigureAwait(false));
                await using var activeClientDisposal = activeClient.ConfigureAwait(false);
                var before = await activeClient.SendRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/before-reload" }, timeout.Token).ConfigureAwait(false);
                var beforeSubject = activeClient.RemoteCertificateSubject;
                TestCertificates.WriteSelfSignedPfx(Path.Combine(dataDirectory, "certs", "home.pfx"), "home-reloaded.test");
                var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
                var afterOnActiveConnection = await activeClient.SendRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/after-reload-active" }, timeout.Token).ConfigureAwait(false);
                var newClient = (await Http2TestClient.ConnectAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(dataDirectory, "certs", "home.pfx"), allowNameMismatch: true), timeout.Token).ConfigureAwait(false));
                await using var newClientDisposal = newClient.ConfigureAwait(false);
                var newSubject = newClient.RemoteCertificateSubject;
                var afterOnNewConnection = await newClient.SendRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/after-reload-new" }, timeout.Token).ConfigureAwait(false);
                ProxyConfigurationReloadResultAssertions.Reloaded(reload, string.Join("; ", reload.Errors));
                AssertEx.True(beforeSubject.Contains("CN=home.test", StringComparison.Ordinal), beforeSubject);
                AssertEx.Equal(203, before.StatusCode);
                AssertEx.Equal(203, afterOnActiveConnection.StatusCode);
                AssertEx.Equal(203, afterOnNewConnection.StatusCode);
                AssertEx.True(newSubject.Contains("CN=home-reloaded.test", StringComparison.Ordinal), newSubject);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task FailedHttp2CertificateReloadPreservesPreviousActiveCertificateAsync()
    {
        var dataDirectory = CreateDataDirectory();
        var proxyPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            WriteCertificateConfig(dataDirectory);
            var originalCertificate = TestCertificates.PinServerCertificate(Path.Combine(dataDirectory, "certs", "home.pfx"));
            SiteWithStaticRoute(dataDirectory, proxyPort, 0);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var beforeClient = (await Http2TestClient.ConnectAsync(proxyPort, originalCertificate, timeout.Token).ConfigureAwait(false));
                await using var beforeClientDisposal = beforeClient.ConfigureAwait(false);
                var beforeSubject = beforeClient.RemoteCertificateSubject;
                TestCertificates.WriteSelfSignedPfx(Path.Combine(dataDirectory, "certs", "home.pfx"), "home-reloaded.test");
                await File.WriteAllTextAsync(Path.Combine(dataDirectory, "config", "sites", "broken.json"), "{ nope").ConfigureAwait(false);
                var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
                var afterClient = (await Http2TestClient.ConnectAsync(proxyPort, originalCertificate, timeout.Token).ConfigureAwait(false));
                await using var afterClientDisposal = afterClient.ConfigureAwait(false);
                var afterSubject = afterClient.RemoteCertificateSubject;
                var response = await afterClient.SendRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/after-failed-reload" }, timeout.Token).ConfigureAwait(false);
                ProxyConfigurationReloadResultAssertions.Failed(reload);
                AssertEx.True(beforeSubject.Contains("CN=home.test", StringComparison.Ordinal), beforeSubject);
                AssertEx.True(afterSubject.Contains("CN=home.test", StringComparison.Ordinal), afterSubject);
                AssertEx.Equal(203, response.StatusCode);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task RedirectRouteWorksOverHttp2Async()
    {
        var result = await RunHttp2ScenarioAsync(SiteWithRedirectRoute, request =>
        {
            request.Authority = "home.test";
            request.Path = "/old?id=1";
        }).ConfigureAwait(false);
        AssertEx.Equal(308, result.Response.StatusCode);
        AssertEx.Equal("/new?id=1", result.Response.Header("location"));
    }

    public static async Task MaintenanceRouteWorksOverHttp2Async()
    {
        var result = await RunHttp2ScenarioAsync(SiteWithMaintenanceRoute, request =>
        {
            request.Authority = "home.test";
            request.Path = "/maintenance";
        }).ConfigureAwait(false);
        AssertEx.Equal(503, result.Response.StatusCode);
        AssertEx.Equal("maintenance", result.Response.BodyText);
    }

    public static async Task HeadReturnsHeadersWithoutBodyAsync()
    {
        var result = await RunHttp2ScenarioAsync((dataDirectory, proxyPort, upstreamPort) => WriteHttpsProxySite(dataDirectory, proxyPort, upstreamPort), request =>
        {
            request.Method = "HEAD";
            request.Authority = "home.test";
            request.Path = "/head";
        }, upstreamResponse: "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 5\r\nX-Head: yes\r\n\r\nhello").ConfigureAwait(false);
        AssertEx.Equal(200, result.Response.StatusCode);
        AssertEx.Equal("yes", result.Response.Header("x-head"));
        AssertEx.Equal("", result.Response.BodyText);
    }

    public static async Task CacheWorksOverHttp2Async()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = CreateDataDirectory();
        try
        {
            WriteHttpsProxySite(dataDirectory, proxyPort, upstreamPort, routeExtraJson: """
                  "cache": {
                    "enabled": true,
                    "maxEntryBytes": 4096,
                    "maxTotalBytes": 8192,
                    "defaultTtlSeconds": 60,
                    "respectOriginCacheControl": true
                  },
                """);
            WriteCertificateConfig(dataDirectory);
            var upstreamTask = RunSingleResponseUpstreamAsync(upstreamPort, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 9\r\nCache-Control: max-age=60\r\n\r\ncache-hit", timeout.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var client = (await Http2TestClient.ConnectAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(dataDirectory, "certs", "home.pfx")), timeout.Token).ConfigureAwait(false));
                await using var clientDisposal = client.ConfigureAwait(false);
                var first = await client.SendRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/cache" }, timeout.Token).ConfigureAwait(false);
                var second = await client.SendRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/cache" }, timeout.Token).ConfigureAwait(false);
                var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
                AssertEx.Equal(200, first.StatusCode);
                AssertEx.Equal(200, second.StatusCode);
                AssertEx.Equal("cache-hit", second.BodyText);
                AssertEx.True(second.Headers.ContainsKey("age"));
                AssertEx.True(upstreamRequest.StartsWith("GET /cache HTTP/1.1", StringComparison.Ordinal), upstreamRequest);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task RetryWorksForHttp2ProxyRequestsAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var firstUpstreamPort = GetFreeTcpPort();
        var secondUpstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = CreateDataDirectory();
        try
        {
            WriteHttpsRetrySite(dataDirectory, proxyPort, firstUpstreamPort, secondUpstreamPort);
            WriteCertificateConfig(dataDirectory);
            ConfigurationTests.WriteOperationalConfig(dataDirectory, upstreamConnectTimeoutMs: 150, upstreamResponseHeadTimeoutMs: 500, certificateId: "home-cert", certificatePath: "certs/home.pfx");
            var upstreamTask = RunSingleResponseUpstreamAsync(secondUpstreamPort, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\n\r\nretried", timeout.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var client = (await Http2TestClient.ConnectAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(dataDirectory, "certs", "home.pfx")), timeout.Token).ConfigureAwait(false));
                await using var clientDisposal = client.ConfigureAwait(false);
                var response = await client.SendRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/retry" }, timeout.Token).ConfigureAwait(false);
                var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
                AssertEx.Equal(200, response.StatusCode);
                AssertEx.Equal("retried", response.BodyText);
                AssertEx.True(upstreamRequest.StartsWith("GET /retry HTTP/1.1", StringComparison.Ordinal), upstreamRequest);
                AssertEx.True(metrics.Resilience.RetryAttempts >= 1, metrics.Resilience.RetryAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task ExtendedConnectIsRejectedAsync()
    {
        var result = await RunHttp2ScenarioAsync(SiteWithStaticRoute, request =>
        {
            request.Method = "CONNECT";
            request.Authority = "home.test";
            request.Path = "/socket";
            request.Headers.Add((":protocol", "websocket"));
        }).ConfigureAwait(false);
        AssertEx.Equal(400, result.Response.StatusCode);
        AssertEx.True(result.Metrics.Http2.ProtocolErrors.ContainsKey("invalid_pseudo_header") || result.Metrics.Http2.ProtocolErrors.ContainsKey("extended_connect_unsupported"));
    }

    public static async Task ConcurrentStreamsReachDifferentRoutesAsync()
    {
        var result = await RunHttp2ManualScenarioAsync(SiteWithTwoStaticRoutes, async (client, _, cancellationToken) => await client.SendRequestsBeforeReadingAsync([new Http2RequestSpec { Authority = "home.test", Path = "/one" }, new Http2RequestSpec { Authority = "home.test", Path = "/two" }], cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        AssertEx.Equal(2, result.Value.Count);
        AssertEx.Equal(200, result.Value[0].StatusCode);
        AssertEx.Equal("one", result.Value[0].BodyText);
        AssertEx.Equal(200, result.Value[1].StatusCode);
        AssertEx.Equal("two", result.Value[1].BodyText);
        AssertEx.Equal(0L, result.Metrics.Http2.ActiveStreams);
    }

    public static async Task DataBeforeHeadersIsRejectedSafelyAsync()
    {
        var result = await RunHttp2ManualScenarioAsync(SiteWithStaticRoute, async (client, _, cancellationToken) => await client.SendDataBeforeHeadersAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        AssertEx.Equal(0, result.Value.StatusCode);
        AssertEx.Equal(1L, result.Metrics.Http2.ProtocolErrors["unexpected_data"]);
        AssertEx.Equal(0L, result.Metrics.Http2.ActiveStreams);
    }

    public static async Task ContinuationHeaderFragmentationIsAcceptedAsync()
    {
        var result = await RunHttp2ManualScenarioAsync(SiteWithStaticRoute, async (client, _, cancellationToken) => await client.SendFragmentedHeadersRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/static" }, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        AssertEx.Equal(203, result.Value.StatusCode);
        AssertEx.Equal("static-h2", result.Value.BodyText);
        AssertEx.Equal(0L, result.Metrics.Http2.ActiveStreams);
    }

    public static async Task RstStreamReleasesStateAndKeepsConnectionUsableAsync()
    {
        var result = await RunHttp2ManualScenarioAsync(SiteWithStaticRoute, async (client, _, cancellationToken) => await client.SendHeadersThenResetThenRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/static" }, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        AssertEx.Equal(203, result.Value.StatusCode);
        AssertEx.Equal("static-h2", result.Value.BodyText);
        AssertEx.Equal(0L, result.Metrics.Http2.ActiveStreams);
    }

    public static async Task GoAwayStopsNewStreamsSafelyAsync()
    {
        var result = await RunHttp2ManualScenarioAsync(SiteWithStaticRoute, async (client, _, cancellationToken) => await client.SendGoAwayThenRequestAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        AssertEx.True(result.Value);
    }

    public static async Task OversizedHeaderListIsRejectedAsync()
    {
        var result = await RunHttp2ManualScenarioAsync(SiteWithLowHeaderLimit, async (client, _, cancellationToken) =>
        {
            var request = new Http2RequestSpec
            {
                Authority = "home.test",
                Path = "/static"
            };
            request.Headers.Add(("x-too-large", new string ('a', 2048)));
            return await client.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        AssertEx.Equal(0, result.Value.StatusCode);
        AssertEx.Equal(1L, result.Metrics.Http2.ProtocolErrors["header_list_too_large"]);
        AssertEx.Equal(0L, result.Metrics.Http2.ActiveStreams);
    }

    public static async Task MetricsIncludeHttp2CountersAsync()
    {
        var result = await RunHttp2ScenarioAsync(SiteWithStaticRoute, request =>
        {
            request.Authority = "home.test";
            request.Path = "/metrics";
        }).ConfigureAwait(false);
        AssertEx.True(result.Metrics.Http2.AcceptedConnections >= 1, result.Metrics.Http2.AcceptedConnections.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.True(result.Metrics.Http2.Requests >= 1, result.Metrics.Http2.Requests.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.Equal(0L, result.Metrics.Http2.ActiveStreams);
    }

    private static async Task<Http2ScenarioResult> RunHttp2ScenarioAsync(Action<string, int, int> writeSite, Action<Http2RequestSpec> configureRequest, string upstreamResponse = "", bool expectUpstream = false)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = CreateDataDirectory();
        try
        {
            writeSite(dataDirectory, proxyPort, upstreamPort);
            WriteCertificateConfig(dataDirectory);
            var upstreamTask = expectUpstream || !string.IsNullOrEmpty(upstreamResponse) ? RunSingleResponseUpstreamAsync(upstreamPort, upstreamResponse, timeout.Token) : Task.FromResult("");
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                Http2Response response;
                string upstreamRequest;
                SslApplicationProtocol negotiatedProtocol;
                {
                    var client = (await Http2TestClient.ConnectAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(dataDirectory, "certs", "home.pfx")), timeout.Token).ConfigureAwait(false));
                    await using var clientDisposal = client.ConfigureAwait(false);
                    var request = new Http2RequestSpec();
                    configureRequest(request);
                    response = await client.SendRequestAsync(request, timeout.Token).ConfigureAwait(false);
                    upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                    negotiatedProtocol = client.NegotiatedProtocol;
                }

                var metricsStore = host.Services.GetRequiredService<ProxyMetrics>();
                await WaitForHttp2StreamsToDrainAsync(metricsStore, timeout.Token).ConfigureAwait(false);
                var metrics = metricsStore.Snapshot();
                var diagnostics = host.Services.GetRequiredService<RecentRequestDiagnosticsStore>().Recent(50);
                return new Http2ScenarioResult(response, upstreamRequest, metrics, diagnostics, negotiatedProtocol);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    private static async Task<Http2ManualScenarioResult<T>> RunHttp2ManualScenarioAsync<T>(Action<string, int, int> writeSite, Func<Http2TestClient, IHost, CancellationToken, Task<T>> exercise)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = CreateDataDirectory();
        try
        {
            writeSite(dataDirectory, proxyPort, upstreamPort);
            WriteCertificateConfig(dataDirectory);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                T value;
                {
                    var client = (await Http2TestClient.ConnectAsync(proxyPort, TestCertificates.PinServerCertificate(Path.Combine(dataDirectory, "certs", "home.pfx")), timeout.Token).ConfigureAwait(false));
                    await using var clientDisposal = client.ConfigureAwait(false);
                    value = await exercise(client, host, timeout.Token).ConfigureAwait(false);
                }

                var metricsStore = host.Services.GetRequiredService<ProxyMetrics>();
                await WaitForHttp2StreamsToDrainAsync(metricsStore, timeout.Token).ConfigureAwait(false);
                var metrics = metricsStore.Snapshot();
                return new Http2ManualScenarioResult<T>(value, metrics);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    private static Task WaitForHttp2StreamsToDrainAsync(ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        return TestWaiters.WaitForHttp2StreamsToDrainAsync(metrics, cancellationToken);
    }

    private static void WriteHttpsProxySite(string dataDirectory, int proxyPort, int upstreamPort, string host = "*", string listenerProtocols = "http2", string routeExtraJson = "")
    {
        ConfigurationTests.WriteCustomSite(dataDirectory, "h2.json", $$"""
            {
              "name": "h2",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "protocols": "{{listenerProtocols}}",
                  "defaultCertificateId": "home-cert",
                  "http2MaxConcurrentStreams": 32,
                  "http2MaxHeaderListBytes": 32768,
                  "http2MaxFrameSize": 16384,
                  "sniCertificates": [
                    {
                      "hostName": "home.test",
                      "certificateId": "home-cert"
                    }
                  ]
                }
              ],
              "host": "{{host}}",
              "routes": [
                {
                  "name": "h2-proxy",
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

    private static void SiteWithStaticRoute(string dataDirectory, int proxyPort, int upstreamPort)
    {
        _ = upstreamPort;
        ConfigurationTests.WriteCustomSite(dataDirectory, "h2.json", $$"""
            {
              "name": "h2",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "protocols": "http2",
                  "defaultCertificateId": "home-cert",
                  "sniCertificates": [
                    {
                      "hostName": "home.test",
                      "certificateId": "home-cert"
                    }
                  ]
                }
              ],
              "host": "*",
              "routes": [
                {
                  "name": "static",
                  "pathPrefix": "/",
                  "action": "staticResponse",
                  "staticResponse": {
                    "statusCode": 203,
                    "contentType": "text/plain",
                    "body": "static-h2"
                  }
                }
              ]
            }
            """);
    }

    private static void SiteWithTwoStaticRoutes(string dataDirectory, int proxyPort, int upstreamPort)
    {
        _ = upstreamPort;
        ConfigurationTests.WriteCustomSite(dataDirectory, "h2.json", $$"""
            {
              "name": "h2",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "protocols": "http2",
                  "defaultCertificateId": "home-cert",
                  "sniCertificates": [
                    {
                      "hostName": "home.test",
                      "certificateId": "home-cert"
                    }
                  ]
                }
              ],
              "host": "*",
              "routes": [
                {
                  "name": "one",
                  "pathPrefix": "/one",
                  "action": "staticResponse",
                  "staticResponse": {
                    "statusCode": 200,
                    "contentType": "text/plain",
                    "body": "one"
                  }
                },
                {
                  "name": "two",
                  "pathPrefix": "/two",
                  "action": "staticResponse",
                  "staticResponse": {
                    "statusCode": 200,
                    "contentType": "text/plain",
                    "body": "two"
                  }
                }
              ]
            }
            """);
    }

    private static void SiteWithLowHeaderLimit(string dataDirectory, int proxyPort, int upstreamPort)
    {
        _ = upstreamPort;
        ConfigurationTests.WriteCustomSite(dataDirectory, "h2.json", $$"""
            {
              "name": "h2",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "protocols": "http2",
                  "defaultCertificateId": "home-cert",
                  "http2MaxHeaderListBytes": 1024,
                  "sniCertificates": [
                    {
                      "hostName": "home.test",
                      "certificateId": "home-cert"
                    }
                  ]
                }
              ],
              "host": "*",
              "routes": [
                {
                  "name": "static",
                  "pathPrefix": "/",
                  "action": "staticResponse",
                  "staticResponse": {
                    "statusCode": 203,
                    "contentType": "text/plain",
                    "body": "static-h2"
                  }
                }
              ]
            }
            """);
    }

    private static void SiteWithRedirectRoute(string dataDirectory, int proxyPort, int upstreamPort)
    {
        _ = upstreamPort;
        ConfigurationTests.WriteCustomSite(dataDirectory, "h2.json", $$"""
            {
              "name": "h2",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "protocols": "http2",
                  "defaultCertificateId": "home-cert",
                  "sniCertificates": [
                    {
                      "hostName": "home.test",
                      "certificateId": "home-cert"
                    }
                  ]
                }
              ],
              "host": "*",
              "routes": [
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
              ]
            }
            """);
    }

    private static void SiteWithMaintenanceRoute(string dataDirectory, int proxyPort, int upstreamPort)
    {
        _ = upstreamPort;
        ConfigurationTests.WriteCustomSite(dataDirectory, "h2.json", $$"""
            {
              "name": "h2",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "protocols": "http2",
                  "defaultCertificateId": "home-cert",
                  "sniCertificates": [
                    {
                      "hostName": "home.test",
                      "certificateId": "home-cert"
                    }
                  ]
                }
              ],
              "host": "*",
              "routes": [
                {
                  "name": "maintenance",
                  "pathPrefix": "/maintenance",
                  "action": "proxy",
                  "maintenance": {
                    "enabled": true,
                    "body": "maintenance"
                  },
                  "upstreams": [
                    {
                      "name": "unused",
                      "address": "127.0.0.1",
                      "port": 1
                    }
                  ]
                }
              ]
            }
            """);
    }

    private static void WriteHttpsRetrySite(string dataDirectory, int proxyPort, int firstUpstreamPort, int secondUpstreamPort)
    {
        ConfigurationTests.WriteCustomSite(dataDirectory, "h2.json", $$"""
            {
              "name": "h2",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "protocols": "http2",
                  "defaultCertificateId": "home-cert",
                  "sniCertificates": [
                    {
                      "hostName": "home.test",
                      "certificateId": "home-cert"
                    }
                  ]
                }
              ],
              "host": "*",
              "routes": [
                {
                  "name": "retry",
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
                      "name": "down",
                      "address": "127.0.0.1",
                      "port": {{firstUpstreamPort}}
                    },
                    {
                      "name": "up",
                      "address": "127.0.0.1",
                      "port": {{secondUpstreamPort}}
                    }
                  ]
                }
              ]
            }
            """);
    }

    private static void WriteCertificateConfig(string dataDirectory)
    {
        TestCertificates.WriteSelfSignedPfx(Path.Combine(dataDirectory, "certs", "home.pfx"), "home.test");
        ConfigurationTests.WriteOperationalConfig(dataDirectory, certificateId: "home-cert", certificatePath: "certs/home.pfx");
    }

    private static IHost BuildProxyHost(string dataDirectory)
    {
        return Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Mdrava:DataDirectory"] = dataDirectory });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) =>
        {
            services.AddProxyDataPlane(context.Configuration);
        }).Build();
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
            var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(response))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
            }

            return request;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var bytes = new MemoryStream();
        var headText = "";
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            var data = bytes.ToArray();
            var headEnd = IndexOfHeaderEnd(data);
            if (headEnd < 0)
            {
                continue;
            }

            headText = Encoding.ASCII.GetString(data, 0, headEnd);
            var contentLength = ContentLength(headText);
            var bodyBytes = data.Length - headEnd - 4;
            while (bodyBytes < contentLength)
            {
                read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                bodyBytes += read;
            }

            break;
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static int ContentLength(string headText)
    {
        foreach (var line in headText.Split("\r\n", StringSplitOptions.None))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return int.TryParse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        return 0;
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

    private static async Task<string> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var builder = new StringBuilder();
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return builder.ToString();
    }

    private static int GetFreeTcpPort() => TestPortAllocator.GetFreeTcpPort();
    private static string CreateDataDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"mdrava-h2-{Guid.NewGuid():N}");
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class Http2RequestSpec
    {
        public string Method { get; set; } = "GET";
        public string Scheme { get; set; } = "https";
        public string Authority { get; set; } = "home.test";
        public string Path { get; set; } = "/";
        public List<(string Name, string Value)> Headers { get; } = [];
        public List<(string Name, string Value)> HuffmanValueHeaders { get; } = [];
        public byte[] Body { get; set; } = [];
        public bool DuplicatePathPseudoHeader { get; set; }
    }

    private sealed record Http2ScenarioResult(Http2Response Response, string UpstreamRequest, ProxyMetricsSnapshot Metrics, IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Diagnostics, SslApplicationProtocol NegotiatedProtocol);
    private sealed record Http2ManualScenarioResult<T>(T Value, ProxyMetricsSnapshot Metrics);
    private sealed class Http2Response
    {
        public Http2Response(int statusCode, IReadOnlyDictionary<string, string> headers, byte[] body)
        {
            StatusCode = statusCode;
            Headers = headers;
            Body = body;
        }

        public int StatusCode { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }
        public byte[] Body { get; }
        public string BodyText => Encoding.UTF8.GetString(Body);

        public string Header(string name)
        {
            return Headers.TryGetValue(name, out var value) ? value : "";
        }
    }

    private sealed class Http2TestClient : IAsyncDisposable
    {
        private static readonly byte[] ClientPreface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        private readonly TcpClient _client;
        private readonly SslStream _stream;
        private int _nextStreamId = 1;
        private Http2TestClient(TcpClient client, SslStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public SslApplicationProtocol NegotiatedProtocol => _stream.NegotiatedApplicationProtocol;

        public string RemoteCertificateSubject
        {
            get
            {
                return _stream.RemoteCertificate?.Subject ?? "";
            }
        }

        public static async Task<Http2TestClient> ConnectAsync(int port, RemoteCertificateValidationCallback certificateValidation, CancellationToken cancellationToken)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
            var stream = new SslStream(client.GetStream(), false, certificateValidation);
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "home.test", EnabledSslProtocols = SslProtocols.None, ApplicationProtocols = [SslApplicationProtocol.Http2] }, cancellationToken).ConfigureAwait(false);
            var http2 = new Http2TestClient(client, stream);
            await http2.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return http2;
        }

        public async Task<Http2Response> SendRequestAsync(Http2RequestSpec request, CancellationToken cancellationToken)
        {
            var streamId = _nextStreamId;
            _nextStreamId += 2;
            var headers = EncodeRequestHeaders(request);
            await WriteFrameAsync(Http2TestFrameType.Headers, request.Body.Length == 0 ? (byte)(Http2TestFlags.EndHeaders | Http2TestFlags.EndStream) : Http2TestFlags.EndHeaders, streamId, headers, cancellationToken).ConfigureAwait(false);
            if (request.Body.Length > 0)
            {
                await WriteFrameAsync(Http2TestFrameType.Data, Http2TestFlags.EndStream, streamId, request.Body, cancellationToken).ConfigureAwait(false);
            }

            return await ReadResponseAsync(streamId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<Http2Response>> SendRequestsBeforeReadingAsync(IReadOnlyList<Http2RequestSpec> requests, CancellationToken cancellationToken)
        {
            List<int> streamIds = [];
            foreach (var request in requests)
            {
                var streamId = _nextStreamId;
                _nextStreamId += 2;
                streamIds.Add(streamId);
                var headers = EncodeRequestHeaders(request);
                await WriteFrameAsync(Http2TestFrameType.Headers, request.Body.Length == 0 ? (byte)(Http2TestFlags.EndHeaders | Http2TestFlags.EndStream) : Http2TestFlags.EndHeaders, streamId, headers, cancellationToken).ConfigureAwait(false);
                if (request.Body.Length > 0)
                {
                    await WriteFrameAsync(Http2TestFrameType.Data, Http2TestFlags.EndStream, streamId, request.Body, cancellationToken).ConfigureAwait(false);
                }
            }

            return await ReadResponsesAsync(streamIds, cancellationToken).ConfigureAwait(false);
        }

        public async Task<Http2Response> SendDataBeforeHeadersAsync(CancellationToken cancellationToken)
        {
            var streamId = _nextStreamId;
            _nextStreamId += 2;
            await WriteFrameAsync(Http2TestFrameType.Data, Http2TestFlags.EndStream, streamId, "bad"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            return await ReadResponseAsync(streamId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<Http2Response> SendFragmentedHeadersRequestAsync(Http2RequestSpec request, CancellationToken cancellationToken)
        {
            var streamId = _nextStreamId;
            _nextStreamId += 2;
            var headers = EncodeRequestHeaders(request);
            var split = Math.Max(1, headers.Length / 2);
            await WriteFrameAsync(Http2TestFrameType.Headers, Http2TestFlags.EndStream, streamId, headers.AsMemory(0, split), cancellationToken).ConfigureAwait(false);
            await WriteFrameAsync(Http2TestFrameType.Continuation, Http2TestFlags.EndHeaders, streamId, headers.AsMemory(split), cancellationToken).ConfigureAwait(false);
            return await ReadResponseAsync(streamId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<Http2Response> SendHeadersThenResetThenRequestAsync(Http2RequestSpec goodRequest, CancellationToken cancellationToken)
        {
            var resetStreamId = _nextStreamId;
            _nextStreamId += 2;
            var resetHeaders = EncodeRequestHeaders(new Http2RequestSpec { Authority = "home.test", Path = "/reset" });
            await WriteFrameAsync(Http2TestFrameType.Headers, Http2TestFlags.EndHeaders, resetStreamId, resetHeaders, cancellationToken).ConfigureAwait(false);
            var resetPayload = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(resetPayload, 0x8);
            await WriteFrameAsync(Http2TestFrameType.RstStream, 0, resetStreamId, resetPayload, cancellationToken).ConfigureAwait(false);
            return await SendRequestAsync(goodRequest, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> SendGoAwayThenRequestAsync(CancellationToken cancellationToken)
        {
            var payload = new byte[8];
            await WriteFrameAsync(Http2TestFrameType.GoAway, 0, 0, payload, cancellationToken).ConfigureAwait(false);
            try
            {
                var response = await SendRequestAsync(new Http2RequestSpec { Authority = "home.test", Path = "/after-goaway" }, cancellationToken).ConfigureAwait(false);
                return response.StatusCode == 0;
            }
            catch (IOException)
            {
                return true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _client.Dispose();
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await _stream.WriteAsync(ClientPreface, cancellationToken).ConfigureAwait(false);
            await WriteFrameAsync(Http2TestFrameType.Settings, 0, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            while (true)
            {
                var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame.Type != Http2TestFrameType.Settings)
                {
                    continue;
                }

                if ((frame.Flags & Http2TestFlags.Ack) == 0)
                {
                    await WriteFrameAsync(Http2TestFrameType.Settings, Http2TestFlags.Ack, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
        }

        private async Task<Http2Response> ReadResponseAsync(int streamId, CancellationToken cancellationToken)
        {
            var headerBlock = new MemoryStream();
            var body = new MemoryStream();
            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            var statusCode = 0;
            while (true)
            {
                var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame.Type == Http2TestFrameType.Settings)
                {
                    if ((frame.Flags & Http2TestFlags.Ack) == 0)
                    {
                        await WriteFrameAsync(Http2TestFrameType.Settings, Http2TestFlags.Ack, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (frame.StreamId != streamId)
                {
                    continue;
                }

                if (frame.Type == Http2TestFrameType.Headers || frame.Type == Http2TestFrameType.Continuation)
                {
                    headerBlock.Write(frame.Payload.Span);
                    if ((frame.Flags & Http2TestFlags.EndHeaders) != 0)
                    {
                        foreach (ref var header in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(DecodeHeaders(headerBlock.ToArray())))
                        {
                            if (string.Equals(header.Name, ":status", StringComparison.Ordinal))
                            {
                                statusCode = int.Parse(header.Value, System.Globalization.CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                headers[header.Name] = header.Value;
                            }
                        }
                    }

                    if ((frame.Flags & Http2TestFlags.EndStream) != 0)
                    {
                        return new Http2Response(statusCode, headers, body.ToArray());
                    }
                }
                else if (frame.Type == Http2TestFrameType.Data)
                {
                    body.Write(frame.Payload.Span);
                    if ((frame.Flags & Http2TestFlags.EndStream) != 0)
                    {
                        return new Http2Response(statusCode, headers, body.ToArray());
                    }
                }
                else if (frame.Type == Http2TestFrameType.RstStream)
                {
                    return new Http2Response(0, headers, body.ToArray());
                }
            }
        }

        private async Task<IReadOnlyList<Http2Response>> ReadResponsesAsync(IReadOnlyList<int> streamIds, CancellationToken cancellationToken)
        {
            var pending = streamIds.ToHashSet();
            var builders = streamIds.ToDictionary(static id => id, static _ => new Http2ResponseBuilder());
            Dictionary<int, Http2Response> responses = [];
            while (pending.Count > 0)
            {
                var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame.Type == Http2TestFrameType.Settings)
                {
                    if ((frame.Flags & Http2TestFlags.Ack) == 0)
                    {
                        await WriteFrameAsync(Http2TestFrameType.Settings, Http2TestFlags.Ack, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (!builders.TryGetValue(frame.StreamId, out var builder))
                {
                    continue;
                }

                if (frame.Type == Http2TestFrameType.Headers || frame.Type == Http2TestFrameType.Continuation)
                {
                    builder.HeaderBlock.Write(frame.Payload.Span);
                    if ((frame.Flags & Http2TestFlags.EndHeaders) != 0)
                    {
                        builder.DecodeHeaders();
                    }

                    if ((frame.Flags & Http2TestFlags.EndStream) != 0)
                    {
                        responses[frame.StreamId] = builder.ToResponse();
                        pending.Remove(frame.StreamId);
                    }
                }
                else if (frame.Type == Http2TestFrameType.Data)
                {
                    builder.Body.Write(frame.Payload.Span);
                    if ((frame.Flags & Http2TestFlags.EndStream) != 0)
                    {
                        responses[frame.StreamId] = builder.ToResponse();
                        pending.Remove(frame.StreamId);
                    }
                }
                else if (frame.Type == Http2TestFrameType.RstStream)
                {
                    responses[frame.StreamId] = builder.ToResponse(statusOverride: 0);
                    pending.Remove(frame.StreamId);
                }
            }

            return streamIds.Select(id => responses[id]).ToArray();
        }

        private async Task<Http2TestFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            return await Http2TestFrames.ReadAsync(_stream, cancellationToken).ConfigureAwait(false);
        }

        private async Task WriteFrameAsync(Http2TestFrameType type, byte flags, int streamId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            await Http2TestFrames.WriteAsync(_stream, type, flags, streamId, payload, cancellationToken).ConfigureAwait(false);
        }

        private static byte[] EncodeRequestHeaders(Http2RequestSpec request)
        {
            using var memory = new MemoryStream();
            WriteMethod(memory, request.Method);
            WriteLiteralWithIndexedName(memory, 7, request.Scheme);
            WriteLiteralWithIndexedName(memory, 1, request.Authority);
            WriteLiteralWithIndexedName(memory, 4, request.Path);
            if (request.DuplicatePathPseudoHeader)
            {
                WriteLiteralWithIndexedName(memory, 4, request.Path);
            }

            foreach (var (name, value) in request.Headers)
            {
                WriteLiteral(memory, name, value);
            }

            foreach (var (name, value) in request.HuffmanValueHeaders)
            {
                WriteLiteralWithHuffmanValue(memory, name, value);
            }

            return memory.ToArray();
        }

        private static void WriteMethod(Stream stream, string method)
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                stream.WriteByte(0x82);
                return;
            }

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                stream.WriteByte(0x83);
                return;
            }

            WriteLiteralWithIndexedName(stream, 2, method);
        }

        private static void WriteLiteralWithIndexedName(Stream stream, int nameIndex, string value)
        {
            WriteInteger(stream, 0, 4, nameIndex);
            WriteString(stream, value);
        }

        private static void WriteLiteral(Stream stream, string name, string value)
        {
            stream.WriteByte(0);
            WriteString(stream, name);
            WriteString(stream, value);
        }

        private static void WriteLiteralWithHuffmanValue(Stream stream, string name, string value)
        {
            stream.WriteByte(0);
            WriteString(stream, name);
            WriteHuffmanString(stream, value);
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            WriteInteger(stream, 0, 7, bytes.Length);
            stream.Write(bytes);
        }

        private static void WriteHuffmanString(Stream stream, string value)
        {
            var bytes = EncodeHuffmanAscii(value);
            WriteInteger(stream, 0x80, 7, bytes.Length);
            stream.Write(bytes);
        }

        private static byte[] EncodeHuffmanAscii(string value)
        {
            using var memory = new MemoryStream();
            var pendingBits = 0UL;
            var pendingLength = 0;
            foreach (var character in value)
            {
                var code = HuffmanCode(character, out var codeLength);
                pendingBits = (pendingBits << codeLength) | code;
                pendingLength += codeLength;
                while (pendingLength >= 8)
                {
                    var shift = pendingLength - 8;
                    memory.WriteByte((byte)(pendingBits >> shift));
                    pendingBits &= shift == 0 ? 0 : (1UL << shift) - 1;
                    pendingLength -= 8;
                }
            }

            if (pendingLength > 0)
            {
                pendingBits <<= 8 - pendingLength;
                pendingBits |= (1UL << (8 - pendingLength)) - 1;
                memory.WriteByte((byte)pendingBits);
            }

            return memory.ToArray();
        }

        private static ulong HuffmanCode(char character, out int length)
        {
            (ulong Code, int Length) value = character switch
            {
                'a' => (0x3, 5),
                'd' => (0x24, 6),
                'm' => (0x29, 6),
                'r' => (0x2c, 6),
                'v' => (0x77, 7),
                _ => throw new InvalidOperationException($"No test Huffman code for '{character}'.")};
            length = value.Length;
            return value.Code;
        }

        private static void WriteInteger(Stream stream, byte prefix, int prefixBits, int value)
        {
            var maxPrefix = (1 << prefixBits) - 1;
            if (value < maxPrefix)
            {
                stream.WriteByte((byte)(prefix | value));
                return;
            }

            stream.WriteByte((byte)(prefix | maxPrefix));
            value -= maxPrefix;
            while (value >= 128)
            {
                stream.WriteByte((byte)(value % 128 + 128));
                value /= 128;
            }

            stream.WriteByte((byte)value);
        }

        private static List<(string Name, string Value)> DecodeHeaders(byte[] block)
        {
            List<(string Name, string Value)> headers = [];
            List<(string Name, string Value)> dynamicTable = [];
            var offset = 0;
            while (offset < block.Length)
            {
                var current = block[offset];
                if ((current & 0x80) != 0)
                {
                    var index = DecodeInteger(block, 7, ref offset);
                    headers.Add(GetHeader(index, dynamicTable));
                    continue;
                }

                if ((current & 0x40) != 0)
                {
                    var literal = DecodeLiteral(block, 6, ref offset, dynamicTable);
                    dynamicTable.Insert(0, literal);
                    headers.Add(literal);
                    continue;
                }

                if ((current & 0x20) != 0)
                {
                    _ = DecodeInteger(block, 5, ref offset);
                    continue;
                }

                headers.Add(DecodeLiteral(block, 4, ref offset, dynamicTable));
            }

            return headers;
        }

        private static (string Name, string Value) DecodeLiteral(byte[] block, int prefixBits, ref int offset, IReadOnlyList<(string Name, string Value)> dynamicTable)
        {
            var nameIndex = DecodeInteger(block, prefixBits, ref offset);
            var name = nameIndex == 0 ? ReadString(block, ref offset) : GetHeader(nameIndex, dynamicTable).Name;
            var value = ReadString(block, ref offset);
            return (name, value);
        }

        private static string ReadString(byte[] block, ref int offset)
        {
            if ((block[offset] & 0x80) != 0)
            {
                throw new InvalidOperationException("The test HPACK decoder does not support Huffman strings.");
            }

            var length = DecodeInteger(block, 7, ref offset);
            var value = Encoding.ASCII.GetString(block, offset, length);
            offset += length;
            return value;
        }

        private static int DecodeInteger(byte[] block, int prefixBits, ref int offset)
        {
            var mask = (1 << prefixBits) - 1;
            var value = block[offset++] & mask;
            if (value < mask)
            {
                return value;
            }

            var multiplier = 0;
            while (offset < block.Length)
            {
                var next = block[offset++];
                value += (next & 0x7f) << multiplier;
                if ((next & 0x80) == 0)
                {
                    break;
                }

                multiplier += 7;
            }

            return value;
        }

        private static (string Name, string Value) GetHeader(int index, IReadOnlyList<(string Name, string Value)> dynamicTable)
        {
            if (index > 0 && index < StaticTable.Length)
            {
                return StaticTable[index];
            }

            var dynamicIndex = index - StaticTable.Length;
            return dynamicTable[dynamicIndex];
        }

        private static readonly (string Name, string Value)[] StaticTable = [("", ""), (":authority", ""), (":method", "GET"), (":method", "POST"), (":path", "/"), (":path", "/index.html"), (":scheme", "http"), (":scheme", "https"), (":status", "200"), (":status", "204"), (":status", "206"), (":status", "304"), (":status", "400"), (":status", "404"), (":status", "500"), ("accept-charset", ""), ("accept-encoding", "gzip, deflate"), ("accept-language", ""), ("accept-ranges", ""), ("accept", ""), ("access-control-allow-origin", ""), ("age", ""), ("allow", ""), ("authorization", ""), ("cache-control", ""), ("content-disposition", ""), ("content-encoding", ""), ("content-language", ""), ("content-length", ""), ("content-location", ""), ("content-range", ""), ("content-type", ""), ("cookie", ""), ("date", ""), ("etag", ""), ("expect", ""), ("expires", ""), ("from", ""), ("host", ""), ("if-match", ""), ("if-modified-since", ""), ("if-none-match", ""), ("if-range", ""), ("if-unmodified-since", ""), ("last-modified", ""), ("link", ""), ("location", ""), ("max-forwards", ""), ("proxy-authenticate", ""), ("proxy-authorization", ""), ("range", ""), ("referer", ""), ("refresh", ""), ("retry-after", ""), ("server", ""), ("set-cookie", ""), ("strict-transport-security", ""), ("transfer-encoding", ""), ("user-agent", ""), ("vary", ""), ("via", ""), ("www-authenticate", "")];
        private sealed class Http2ResponseBuilder
        {
            public MemoryStream HeaderBlock { get; } = new();
            public MemoryStream Body { get; } = new();
            public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int StatusCode { get; private set; }

            public void DecodeHeaders()
            {
                foreach (ref var header in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Http2TestClient.DecodeHeaders(HeaderBlock.ToArray())))
                {
                    if (string.Equals(header.Name, ":status", StringComparison.Ordinal))
                    {
                        StatusCode = int.Parse(header.Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        Headers[header.Name] = header.Value;
                    }
                }
            }

            public Http2Response ToResponse(int? statusOverride = null)
            {
                return new Http2Response(statusOverride ?? StatusCode, Headers, Body.ToArray());
            }
        }
    }
}
