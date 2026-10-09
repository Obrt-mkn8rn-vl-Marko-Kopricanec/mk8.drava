#pragma warning disable CA1416
using System.Collections.Concurrent;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class UpstreamHttp3Tests
{
    private static readonly SslApplicationProtocol Http3Alpn = new("h3");
    public static void Http3UpstreamRequiresHttps()
    {
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, OptionsWithUpstream("http", RuntimeUpstreamProtocol.Http3));
        AssertEx.True(validation.Failed);
        AssertEx.True(AssertEx.NotNull(validation.Failures).Any(static failure => failure.Contains("HTTP/3 upstreams require scheme 'https'", StringComparison.Ordinal)), string.Join("; ", validation.Failures ?? []));
    }

    public static async Task Http3UpstreamConfigParsesAndValidatesAsync()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteCustomSite(temp.Path, "upstream-h3.json", SiteJson(proxyPort: 18080, upstreamPort: 18443));
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var upstream = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Routes[0].Upstreams[0];
        AssertEx.Equal(RuntimeUpstreamProtocol.Http3, upstream.Protocol);
        AssertEx.Equal("https", upstream.Scheme);
    }

    public static async Task Http3EffectiveProjectionReportsReusedMultiplexedPoolingAsync()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteCustomSite(temp.Path, "upstream-h3.json", SiteJson(proxyPort: 18080, upstreamPort: 18443));
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot));
        AssertEx.True(projection.Http3.UpstreamHttp3Configured);
        AssertEx.Equal("reused_multiplexed", projection.Http3.UpstreamPoolingMode);
        AssertEx.True(projection.Http3.UpstreamMultiplexingEnabled);
        AssertEx.Equal(8, projection.Http3.UpstreamMaxStreamsPerConnection);
        AssertEx.Equal("", projection.Http3.UpstreamPoolingLimitationReason);
    }

    public static void PoolKeyDiffersForHttp1Http2AndHttp3()
    {
        var http1 = Upstream(5001, RuntimeUpstreamProtocol.Http1);
        var http2 = Upstream(5001, RuntimeUpstreamProtocol.Http2);
        var http3 = Upstream(5001, RuntimeUpstreamProtocol.Http3);
        AssertEx.False(string.Equals(UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(http1)), Http3UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(http3)), StringComparison.Ordinal));
        AssertEx.False(string.Equals(UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(http2)), Http3UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(http3)), StringComparison.Ordinal));
    }

    public static void PoolKeyIncludesHttp3SniAndValidation()
    {
        var first = Upstream(5001, RuntimeUpstreamProtocol.Http3, validateCertificate: true, sniHost: "one.test");
        var second = Upstream(5001, RuntimeUpstreamProtocol.Http3, validateCertificate: true, sniHost: "two.test");
        var third = Upstream(5001, RuntimeUpstreamProtocol.Http3, validateCertificate: false, sniHost: "one.test");
        AssertEx.False(string.Equals(Http3UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(first)), Http3UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(second)), StringComparison.Ordinal));
        AssertEx.False(string.Equals(Http3UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(first)), Http3UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(third)), StringComparison.Ordinal));
    }

    public static async Task Http3UpstreamProxyMapsHeadersQueryAndResponseAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunProxyScenarioAsync("/api/users?id=1", 200, [("content-length", "7"), ("x-upstream", "h3")], Encoding.ASCII.GetBytes("h3-body"), requestHeaders: "Connection: keep-alive\r\nKeep-Alive: timeout=5\r\n").ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.ClientResponse.EndsWith("h3-body", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("GET", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal("https", result.Upstream.RequestHeaders[":scheme"]);
        AssertEx.Equal("/api/users?id=1", result.Upstream.RequestHeaders[":path"]);
        AssertEx.False(result.Upstream.RequestHeaders.ContainsKey("connection"));
        AssertEx.False(result.Upstream.RequestHeaders.ContainsKey("keep-alive"));
    }

    public static async Task SequentialHttp3UpstreamRequestsReuseConnectionAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunReusableProxyScenarioAsync(requestCount: 2, concurrent: false).ConfigureAwait(false);
        AssertEx.Equal(1, result.Upstream.ConnectionCount);
        AssertEx.Equal(2, result.Upstream.Requests.Count);
        AssertEx.True(result.FirstClientResponse.Contains("h3-reuse", StringComparison.Ordinal), result.FirstClientResponse);
        AssertEx.True(result.SecondClientResponse.Contains("h3-reuse", StringComparison.Ordinal), result.SecondClientResponse);
        AssertEx.Equal(1, result.Metrics.UpstreamHttp3.PoolConnectionsOpened);
        AssertEx.True(result.Metrics.UpstreamHttp3.PoolConnectionsReused >= 1, result.Metrics.UpstreamHttp3.PoolConnectionsReused.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static async Task ConcurrentHttp3UpstreamRequestsShareConnectionAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunReusableProxyScenarioAsync(requestCount: 2, concurrent: true).ConfigureAwait(false);
        AssertEx.Equal(1, result.Upstream.ConnectionCount);
        AssertEx.Equal(2, result.Upstream.Requests.Count);
        AssertEx.True(result.FirstClientResponse.Contains("h3-reuse", StringComparison.Ordinal), result.FirstClientResponse);
        AssertEx.True(result.SecondClientResponse.Contains("h3-reuse", StringComparison.Ordinal), result.SecondClientResponse);
        AssertEx.Equal(1, result.Metrics.UpstreamHttp3.PoolConnectionsOpened);
        AssertEx.True(result.Metrics.UpstreamHttp3.PoolConnectionsReused >= 1, result.Metrics.UpstreamHttp3.PoolConnectionsReused.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static async Task IdleHttp3UpstreamConnectionsExpireAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunReusableProxyScenarioAsync(requestCount: 2, concurrent: false, upstreamIdleConnectionLifetimeMs: 100, delayBetweenRequests: TimeSpan.FromMilliseconds(175)).ConfigureAwait(false);
        AssertEx.True(result.Upstream.ConnectionCount >= 2, result.Upstream.ConnectionCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.Equal(2, result.Upstream.Requests.Count);
        AssertEx.True(result.Metrics.UpstreamHttp3.PoolConnectionsOpened >= 2, result.Metrics.UpstreamHttp3.PoolConnectionsOpened.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.Equal(0, result.Metrics.UpstreamHttp3.PoolConnectionsReused);
        AssertEx.True(result.Metrics.UpstreamHttp3.PoolConnectionsClosed >= 1, result.Metrics.UpstreamHttp3.PoolConnectionsClosed.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static async Task UpstreamHttp3GoAwayDrainsConnectionWithoutBreakingActiveStreamAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunReusableProxyScenarioAsync(requestCount: 2, concurrent: false, delayBetweenRequests: TimeSpan.FromMilliseconds(200), sendGoAwayAfterFirstRequest: true).ConfigureAwait(false);
        AssertEx.True(result.FirstClientResponse.Contains("h3-reuse", StringComparison.Ordinal), result.FirstClientResponse);
        AssertEx.True(result.SecondClientResponse.Contains("h3-reuse", StringComparison.Ordinal), result.SecondClientResponse);
        AssertEx.True(result.Upstream.ConnectionCount >= 2, result.Upstream.ConnectionCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.Equal(2, result.Upstream.Requests.Count);
        AssertEx.True(result.Metrics.UpstreamHttp3.PoolConnectionsOpened >= 2, result.Metrics.UpstreamHttp3.PoolConnectionsOpened.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.Equal(0, result.Metrics.UpstreamHttp3.PoolConnectionsReused);
    }

    public static async Task UpstreamHttp3PoolStreamLimitExhaustionReturnsSafeFailureAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunReusableProxyScenarioAsync(requestCount: 9, concurrent: true, expectedUpstreamRequestCount: 8, maxIdleUpstreamConnectionsPerUpstream: 1, upstreamPeerMaxBidirectionalStreams: 16, responseDelay: TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        AssertEx.Equal(8, result.Upstream.Requests.Count);
        AssertEx.True(result.ClientResponses.Count(response => response.Contains("200 OK", StringComparison.Ordinal)) >= 8);
        AssertEx.True(result.ClientResponses.Any(response => response.Contains("502 Bad Gateway", StringComparison.Ordinal) || response.Contains("504 Gateway Timeout", StringComparison.Ordinal)), string.Join("\n---\n", result.ClientResponses));
        AssertEx.True(result.Metrics.UpstreamHttp3.StreamLimitRejections >= 1, result.Metrics.UpstreamHttp3.StreamLimitRejections.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static async Task ConcurrentHttp3UpstreamReuseReleasesActiveStreamGaugeAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunReusableProxyScenarioAsync(requestCount: 2, concurrent: true).ConfigureAwait(false);
        AssertEx.True(result.FirstClientResponse.Contains("h3-reuse", StringComparison.Ordinal), result.FirstClientResponse);
        AssertEx.True(result.SecondClientResponse.Contains("h3-reuse", StringComparison.Ordinal), result.SecondClientResponse);
        AssertEx.Equal(1, result.Upstream.ConnectionCount);
        AssertEx.Equal(2, result.Upstream.Requests.Count);
        AssertEx.Equal(0L, result.Metrics.UpstreamHttp3.ActiveStreams);
    }

    public static async Task UpstreamHttp3StreamResetDoesNotPoisonUnrelatedActiveStreamAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunReusableProxyScenarioAsync(requestCount: 2, concurrent: true, resetFirstStreamBeforeResponse: true).ConfigureAwait(false);
        AssertEx.Equal(2, result.Upstream.Requests.Count);
        AssertEx.True(result.ClientResponses.Any(response => response.Contains("200 OK", StringComparison.Ordinal)), string.Join("\n---\n", result.ClientResponses));
        AssertEx.True(result.ClientResponses.Any(response => response.Contains("502 Bad Gateway", StringComparison.Ordinal) || response.Contains("504 Gateway Timeout", StringComparison.Ordinal)), string.Join("\n---\n", result.ClientResponses));
        AssertEx.Equal(0L, result.Metrics.UpstreamHttp3.ActiveStreams);
    }

    public static async Task FailedHttp3UpstreamConnectionDoesNotReceiveNewStreamsAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunReusableProxyScenarioAsync(requestCount: 2, concurrent: false, closeConnectionAfterFirstRequest: true).ConfigureAwait(false);
        AssertEx.True(result.FirstClientResponse.Contains("502 Bad Gateway", StringComparison.Ordinal) || result.FirstClientResponse.Contains("504 Gateway Timeout", StringComparison.Ordinal), result.FirstClientResponse);
        AssertEx.True(result.SecondClientResponse.Contains("h3-reuse", StringComparison.Ordinal), result.SecondClientResponse);
        AssertEx.True(result.Upstream.ConnectionCount >= 2, result.Upstream.ConnectionCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.Equal(2, result.Upstream.Requests.Count);
        AssertEx.Equal(0L, result.Metrics.UpstreamHttp3.ActiveStreams);
    }

    public static async Task Http3UpstreamAlpnFailureDoesNotDowngradeAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var port = GetFreeUdpPort();
        var listener = (await CreateQuicListenerAsync(port, CancellationToken.None, applicationProtocols: [new SslApplicationProtocol("not-h3")]).ConfigureAwait(false));
        await using var listenerDisposal = listener.ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var client = new UpstreamHealthCheckClient(new UpstreamConnectionFactory(), new ProxyMetrics());
        var upstream = Upstream(port, RuntimeUpstreamProtocol.Http3);
        var result = await client.CheckAsync(Target(Route([upstream]), upstream), timeout.Token).ConfigureAwait(false);
        AssertEx.False(result.Healthy, result.Result);
    }

    public static async Task Http3UpstreamMalformedResponseHeadersAreRejectedAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunProxyScenarioAsync("/bad", 200, [("content-length", "2")], Encoding.ASCII.GetBytes("ok"), malformedResponseHeaders: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("502 Bad Gateway", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.Metrics.UpstreamHttp3.ProtocolErrors.ContainsKey("protocol_failure"), "Expected upstream HTTP/3 protocol failure metric.");
    }

    public static async Task Http3UpstreamForwardsRequestBodyAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunProxyScenarioAsync("/submit", 201, [("content-length", "2")], Encoding.ASCII.GetBytes("ok"), method: "POST", requestBody: "hello h3").ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("201 Created", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("POST", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal("8", result.Upstream.RequestHeaders["content-length"]);
        AssertEx.Equal("hello h3", Encoding.ASCII.GetString(result.Upstream.RequestBody));
    }

    public static async Task Http3HealthCheckUsesH3Async()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var port = GetFreeUdpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = RunSingleHttp3UpstreamAsync(port, 204, [], [], timeout.Token);
        var client = new UpstreamHealthCheckClient(new UpstreamConnectionFactory(), new ProxyMetrics());
        var upstream = Upstream(port, RuntimeUpstreamProtocol.Http3);
        var healthy = await client.CheckAsync(Target(Route([upstream]), upstream), timeout.Token).ConfigureAwait(false);
        var observation = await server.WaitAsync(timeout.Token).ConfigureAwait(false);
        AssertEx.True(healthy.Healthy, healthy.Result);
        AssertEx.True(healthy.Result.Contains("HTTP/3 204", StringComparison.Ordinal), healthy.Result);
        AssertEx.Equal("GET", observation.RequestHeaders[":method"]);
        AssertEx.Equal("/health", observation.RequestHeaders[":path"]);
    }

    public static async Task CacheWorksWithHttp3UpstreamAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunProxyScenarioAsync("/cache", 200, [("content-length", "8"), ("cache-control", "max-age=60")], Encoding.ASCII.GetBytes("cache-h3"), routeExtraJson: """
                  "cache": {
                    "enabled": true,
                    "maxEntryBytes": 4096,
                    "maxTotalBytes": 8192,
                    "defaultTtlSeconds": 60,
                    "respectOriginCacheControl": true
                  },
            """, sendSecondRequest: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("cache-h3", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.SecondClientResponse.Contains("cache-h3", StringComparison.Ordinal), result.SecondClientResponse);
        AssertEx.True(result.Metrics.UpstreamHttp3.Requests >= 1, result.Metrics.UpstreamHttp3.Requests.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static async Task MetricsIncludeUpstreamHttp3CountersAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunProxyScenarioAsync("/metrics", 200, [("content-length", "2")], Encoding.ASCII.GetBytes("ok")).ConfigureAwait(false);
        AssertEx.True(result.Metrics.UpstreamHttp3.Requests >= 1, result.Metrics.UpstreamHttp3.Requests.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.True(result.Metrics.UpstreamHttp3.ConnectionAttempts >= 1, result.Metrics.UpstreamHttp3.ConnectionAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.True(result.Metrics.UpstreamHttp3.ConnectionSuccesses >= 1, result.Metrics.UpstreamHttp3.ConnectionSuccesses.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.True(result.Metrics.UpstreamHttp3.PoolConnectionsOpened >= 1, result.Metrics.UpstreamHttp3.PoolConnectionsOpened.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertEx.Equal(0, result.Metrics.UpstreamHttp3.PoolConnectionsReused);
        AssertEx.True(result.Metrics.UpstreamHttp3.ActiveConnections >= 1, result.Metrics.UpstreamHttp3.ActiveConnections.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static async Task Http3UpstreamCloseBeforeResponseHeadersReturnsSafeFailureAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunProxyScenarioAsync("/close-before-headers", 200, [("content-length", "2")], Encoding.ASCII.GetBytes("ok"), closeBeforeResponseHeaders: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("502 Bad Gateway", StringComparison.Ordinal) || result.ClientResponse.Contains("504 Gateway Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("GET", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal("/close-before-headers", result.Upstream.RequestHeaders[":path"]);
        AssertEx.Equal(0L, result.Metrics.UpstreamHttp3.ActiveStreams);
    }

    public static async Task Http3UpstreamCloseAfterResponseHeadersReleasesStreamSlotAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunProxyScenarioAsync("/close-after-headers", 200, [("content-length", "8")], Encoding.ASCII.GetBytes("ignored"), routeExtraJson: RetryJson(), closeAfterResponseHeaders: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("GET", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal(0L, result.Metrics.Resilience.RetryAttempts);
        AssertEx.Equal(0L, result.Metrics.UpstreamHttp3.ActiveStreams);
    }

    public static async Task Http3StreamingPostBodyIsNotRetriedAfterUpstreamFailureAsync()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
        {
            return;
        }

        var result = await RunProxyScenarioAsync("/post-failure", 200, [("content-length", "2")], Encoding.ASCII.GetBytes("ok"), method: "POST", requestBody: "streamed-h3", routeExtraJson: RetryJson(), closeBeforeResponseHeaders: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("502 Bad Gateway", StringComparison.Ordinal) || result.ClientResponse.Contains("504 Gateway Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("POST", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal("streamed-h3", Encoding.ASCII.GetString(result.Upstream.RequestBody));
        AssertEx.Equal(0L, result.Metrics.Resilience.RetryAttempts);
        AssertEx.True(result.Metrics.Resilience.RetrySkipped.Any(static skipped => skipped.Reason is "method" or "request_body"));
        AssertEx.Equal(0L, result.Metrics.UpstreamHttp3.ActiveStreams);
    }

    private static async Task<ProxyScenarioResult> RunProxyScenarioAsync(string target, int statusCode, IReadOnlyList<(string Name, string Value)> responseHeaders, byte[] responseBody, string requestHeaders = "", string method = "GET", string requestBody = "", string routeExtraJson = "", bool sendSecondRequest = false, bool malformedResponseHeaders = false, bool closeBeforeResponseHeaders = false, bool closeAfterResponseHeaders = false)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeUdpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteCustomSite(temp.Path, "upstream-h3.json", SiteJson(proxyPort, upstreamPort, routeExtraJson));
        var upstreamTask = RunSingleHttp3UpstreamAsync(upstreamPort, statusCode, responseHeaders, responseBody, timeout.Token, malformedResponseHeaders, closeBeforeResponseHeaders, closeAfterResponseHeaders);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var requestBodyBytes = Encoding.ASCII.GetBytes(requestBody);
            var contentLengthHeader = requestBodyBytes.Length > 0 ? $"Content-Length: {requestBodyBytes.Length}\r\n" : "";
            var request = $"{method} {target} HTTP/1.1\r\nHost: home.test\r\n{requestHeaders}{contentLengthHeader}Connection: close\r\n\r\n{requestBody}";
            var first = await SendSingleRequestAsync(proxyPort, request, timeout.Token).ConfigureAwait(false);
            var second = sendSecondRequest ? await SendSingleRequestAsync(proxyPort, request, timeout.Token).ConfigureAwait(false) : "";
            var upstream = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            return new ProxyScenarioResult(first, second, upstream, metrics);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string RetryJson()
    {
        return """
                  "retry": {
                    "enabled": true,
                    "maxAttempts": 2,
                    "retryOnConnectFailure": true,
                    "retryMethods": [ "GET", "HEAD" ],
                    "retryBackoffMilliseconds": 0
                  },
        """;
    }

    private static async Task<ReusableProxyScenarioResult> RunReusableProxyScenarioAsync(int requestCount, bool concurrent, int? upstreamIdleConnectionLifetimeMs = null, TimeSpan? delayBetweenRequests = null, bool sendGoAwayAfterFirstRequest = false, int? expectedUpstreamRequestCount = null, int? maxIdleUpstreamConnectionsPerUpstream = null, int? upstreamPeerMaxBidirectionalStreams = null, TimeSpan? responseDelay = null, bool resetFirstStreamBeforeResponse = false, bool closeConnectionAfterFirstRequest = false)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeUdpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteCustomSite(temp.Path, "upstream-h3.json", SiteJson(proxyPort, upstreamPort));
        if (upstreamIdleConnectionLifetimeMs.HasValue || maxIdleUpstreamConnectionsPerUpstream.HasValue)
        {
            ConfigurationTests.WriteOperationalConfig(temp.Path, upstreamIdleConnectionLifetimeMs: upstreamIdleConnectionLifetimeMs ?? 60000, maxIdleUpstreamConnectionsPerUpstream: maxIdleUpstreamConnectionsPerUpstream ?? 16);
        }

        var expectedRequests = expectedUpstreamRequestCount ?? requestCount;
        var upstreamTask = RunReusableHttp3UpstreamAsync(upstreamPort, expectedRequests, [("content-length", "8")], Encoding.ASCII.GetBytes("h3-reuse"), holdResponsesUntilAllRequestsRead: concurrent, sendGoAwayAfterFirstRequest, responseDelay, resetFirstStreamBeforeResponse, closeConnectionAfterFirstRequest, upstreamPeerMaxBidirectionalStreams, timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var request = "GET /reuse HTTP/1.1\r\nHost: home.test\r\nConnection: close\r\n\r\n";
            List<string> responses = [];
            if (concurrent)
            {
                var tasks = Enumerable.Range(0, requestCount).Select(_ => SendSingleRequestAsync(proxyPort, request, timeout.Token)).ToArray();
                responses.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));
            }
            else
            {
                for (var index = 0; index < requestCount; index++)
                {
                    responses.Add(await SendSingleRequestAsync(proxyPort, request, timeout.Token).ConfigureAwait(false));
                    if (delayBetweenRequests.HasValue && index < requestCount - 1)
                    {
                        await Task.Delay(delayBetweenRequests.Value, timeout.Token).ConfigureAwait(false);
                    }
                }
            }

            var upstream = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            return new ReusableProxyScenarioResult(responses.ToArray(), upstream, metrics);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string SiteJson(int proxyPort, int upstreamPort, string routeExtraJson = "")
    {
        return $$"""
        {
          "name": "upstream-h3",
          "listeners": [
            {
              "name": "main",
              "address": "127.0.0.1",
              "port": {{proxyPort}}
            }
          ],
          "host": "*",
          "routes": [
            {
              "name": "app",
              "pathPrefix": "/",
              "action": "proxy",
              {{routeExtraJson}}
              "upstreams": [
                {
                  "name": "h3-upstream",
                  "scheme": "https",
                  "protocol": "http3",
                  "address": "127.0.0.1",
                  "port": {{upstreamPort}},
                  "upstreamTls": {
                    "validateCertificate": false,
                    "sniHost": "upstream.test"
                  }
                }
              ]
            }
          ]
        }
        """;
    }

    private static async Task<Http3UpstreamObservation> RunSingleHttp3UpstreamAsync(int port, int statusCode, IReadOnlyList<(string Name, string Value)> responseHeaders, byte[] responseBody, CancellationToken cancellationToken, bool malformedResponseHeaders = false, bool closeBeforeResponseHeaders = false, bool closeAfterResponseHeaders = false)
    {
        var listener = (await CreateQuicListenerAsync(port, cancellationToken).ConfigureAwait(false));
        await using var listenerDisposal = listener.ConfigureAwait(false);
        try
        {
            var connection = (await listener.AcceptConnectionAsync(cancellationToken).ConfigureAwait(false));
            await using var connectionDisposal = connection.ConfigureAwait(false);
            using var drainStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            List<Task> drains = [];
            try
            {
                while (true)
                {
                    var stream = await connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
                    if (stream.Type != QuicStreamType.Bidirectional)
                    {
                        drains.Add(ObserveAsync(() => DrainAsync(stream, drainStop.Token)));
                        continue;
                    }

                    await using var ownedStream = stream.ConfigureAwait(false);
                    var observation = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (closeBeforeResponseHeaders)
                    {
                        return observation;
                    }

                    await WriteResponseAsync(stream, statusCode, responseHeaders, responseBody, cancellationToken, malformedResponseHeaders, closeAfterResponseHeaders).ConfigureAwait(false);
                    return observation;
                }
            }
            finally
            {
                await drainStop.CancelAsync().ConfigureAwait(false);
                await Task.WhenAll(drains).ConfigureAwait(false);
            }
        }
        catch (Exception exception)when (exception is AuthenticationException or IOException or QuicException)
        {
            return new Http3UpstreamObservation(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], exception.GetType().Name);
        }
    }

    private static async Task<ReusableHttp3UpstreamObservation> RunReusableHttp3UpstreamAsync(int port, int requestCount, IReadOnlyList<(string Name, string Value)> responseHeaders, byte[] responseBody, bool holdResponsesUntilAllRequestsRead, bool sendGoAwayAfterFirstRequest, TimeSpan? responseDelay, bool resetFirstStreamBeforeResponse, bool closeConnectionAfterFirstRequest, int? upstreamPeerMaxBidirectionalStreams, CancellationToken cancellationToken)
    {
        var listener = (await CreateQuicListenerAsync(port, cancellationToken, maxInboundBidirectionalStreams: upstreamPeerMaxBidirectionalStreams ?? 4).ConfigureAwait(false));
        await using var listenerDisposal = listener.ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var requests = new ConcurrentQueue<Http3UpstreamObservation>();
        var connectionTasks = new ConcurrentBag<Task>();
        Task<QuicConnection>? pendingAccept = null;
        var completed = 0;
        var read = 0;
        var connections = 0;
        var allCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resetCanRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var goAwaySent = 0;
        var resetSent = 0;
        var closeSent = 0;
        async Task HandleStreamAsync(QuicConnection connection, QuicStream stream, CancellationToken streamStop, ConcurrentBag<QuicStream> controls)
        {
            await using var ownedStream = stream.ConfigureAwait(false);
            var observation = await ReadRequestAsync(stream, streamStop).ConfigureAwait(false);
            requests.Enqueue(observation);
            var resetThisStream = resetFirstStreamBeforeResponse && Interlocked.Exchange(ref resetSent, 1) == 0;
            if (sendGoAwayAfterFirstRequest && Interlocked.Exchange(ref goAwaySent, 1) == 0)
            {
                controls.Add(await SendGoAwayAsync(connection, streamStop).ConfigureAwait(false));
            }

            if (Interlocked.Increment(ref read) >= requestCount)
            {
                allRead.TrySetResult();
            }

            if (holdResponsesUntilAllRequestsRead)
            {
                await allRead.Task.WaitAsync(streamStop).ConfigureAwait(false);
            }

            if (resetThisStream)
            {
                if (requestCount > 1)
                {
                    await resetCanRun.Task.WaitAsync(streamStop).ConfigureAwait(false);
                }

                stream.Abort(QuicAbortDirection.Write, 0x100);
                if (Interlocked.Increment(ref completed) >= requestCount)
                {
                    allCompleted.TrySetResult();
                }

                return;
            }

            if (closeConnectionAfterFirstRequest && Interlocked.Exchange(ref closeSent, 1) == 0)
            {
                await connection.CloseAsync(0x100, streamStop).ConfigureAwait(false);
                if (Interlocked.Increment(ref completed) >= requestCount)
                {
                    allCompleted.TrySetResult();
                }

                return;
            }

            if (responseDelay.HasValue)
            {
                await Task.Delay(responseDelay.Value, streamStop).ConfigureAwait(false);
            }

            await WriteResponseAsync(stream, 200, responseHeaders, responseBody, streamStop, malformedResponseHeaders: false).ConfigureAwait(false);
            resetCanRun.TrySetResult();
            if (Interlocked.Increment(ref completed) >= requestCount)
            {
                allCompleted.TrySetResult();
            }
        }

        async Task HandleConnectionAsync(QuicConnection connection)
        {
            await using var ownedConnection = connection.ConfigureAwait(false);
            using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            List<Task> children = [];
            var controls = new ConcurrentBag<QuicStream>();
            try
            {
                while (!connectionStop.IsCancellationRequested)
                {
                    var stream = await connection.AcceptInboundStreamAsync(connectionStop.Token).ConfigureAwait(false);
                    children.Add(ObserveAsync(() => stream.Type == QuicStreamType.Bidirectional
                        ? HandleStreamAsync(connection, stream, connectionStop.Token, controls)
                        : DrainAsync(stream, connectionStop.Token)));
                }
            }
            finally
            {
                await connectionStop.CancelAsync().ConfigureAwait(false);
                try
                {
                    await Task.WhenAll(children).ConfigureAwait(false);
                }
                finally
                {
                    foreach (var control in controls) await control.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        try
        {
            while (Volatile.Read(ref completed) < requestCount)
            {
                pendingAccept = listener.AcceptConnectionAsync(stop.Token).AsTask();
                var completedTask = await Task.WhenAny(pendingAccept, allCompleted.Task).ConfigureAwait(false);
                if (completedTask == allCompleted.Task)
                {
                    break;
                }

                var connection = await pendingAccept.ConfigureAwait(false);
                pendingAccept = null;
                Interlocked.Increment(ref connections);
                var task = ObserveAsync(() => HandleConnectionAsync(connection));
                connectionTasks.Add(task);
            }

            await allCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                if (pendingAccept is not null)
                {
                    try
                    {
                        var unassigned = await pendingAccept.ConfigureAwait(false);
                        await unassigned.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
                }
            }
            finally
            {
                await Task.WhenAll(connectionTasks).ConfigureAwait(false);
            }
        }

        return new ReusableHttp3UpstreamObservation(connections, requests.ToArray());
    }

    private static async Task ObserveAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception exception)when (exception is OperationCanceledException or IOException or QuicException)
        {
        }
    }

    private static async ValueTask<DevelopmentOwnedQuicListener> CreateQuicListenerAsync(int port, CancellationToken cancellationToken, IReadOnlyList<SslApplicationProtocol>? applicationProtocols = null, int maxInboundBidirectionalStreams = 4)
    {
        var certificate = CreateServerCertificate("upstream.test");
        try
        {
            var listener = await QuicListener.ListenAsync(new QuicListenerOptions { ListenEndPoint = new IPEndPoint(IPAddress.Loopback, port), ApplicationProtocols = applicationProtocols?.ToList() ?? [Http3Alpn], ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions { ServerAuthenticationOptions = new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls13, ApplicationProtocols = applicationProtocols?.ToList() ?? [Http3Alpn], CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, MaxInboundBidirectionalStreams = maxInboundBidirectionalStreams, MaxInboundUnidirectionalStreams = 4, IdleTimeout = TimeSpan.FromSeconds(5), HandshakeTimeout = TimeSpan.FromSeconds(5), DefaultCloseErrorCode = 0x100, DefaultStreamErrorCode = 0x100 }) }, cancellationToken).ConfigureAwait(false);
            return new DevelopmentOwnedQuicListener(listener, certificate);
        }
        catch { certificate.Dispose(); throw; }
    }

    private static async ValueTask<Http3UpstreamObservation> ReadRequestAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var requestBody = new MemoryStream();
        while (true)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame.EndStream)
            {
                return new Http3UpstreamObservation(requestHeaders, requestBody.ToArray(), null);
            }

            if (frame.Type == Http3Codec.HeadersFrame)
            {
                if (!Http3Codec.TryDecodeHeaderBlock(frame.Payload.Span, maxHeaderBytes: 64 * 1024, out var headers, out var reason))
                {
                    return new Http3UpstreamObservation(requestHeaders, requestBody.ToArray(), reason);
                }

                foreach (var header in headers)
                {
                    requestHeaders[header.Name] = header.Value;
                }

                continue;
            }

            if (frame.Type == Http3Codec.DataFrame)
            {
                requestBody.Write(frame.Payload.Span);
                continue;
            }

            return new Http3UpstreamObservation(requestHeaders, requestBody.ToArray(), "unexpected_frame");
        }
    }

    private static async ValueTask WriteResponseAsync(QuicStream stream, int statusCode, IReadOnlyList<(string Name, string Value)> responseHeaders, ReadOnlyMemory<byte> responseBody, CancellationToken cancellationToken, bool malformedResponseHeaders, bool closeAfterHeaders = false)
    {
        List<ProxyHeaderField> headers = malformedResponseHeaders ? [] : [new(":status", statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        foreach (var header in responseHeaders)
        {
            headers.Add(new ProxyHeaderField(header.Name, header.Value));
        }

        var block = Http3Codec.EncodeHeaderBlock(headers);
        using var head = new MemoryStream();
        Http3Codec.WriteFrame(head, Http3Codec.HeadersFrame, block);
        await stream.WriteAsync(head.ToArray(), completeWrites: responseBody.Length == 0, cancellationToken).ConfigureAwait(false);
        if (closeAfterHeaders)
        {
            return;
        }

        if (responseBody.Length > 0)
        {
            using var body = new MemoryStream();
            Http3Codec.WriteFrame(body, Http3Codec.DataFrame, responseBody.Span);
            await stream.WriteAsync(body.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<QuicStream> SendGoAwayAsync(QuicConnection connection, CancellationToken cancellationToken)
    {
        var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cancellationToken).ConfigureAwait(false);
        try
        {
            using var payload = new MemoryStream();
            Http3Codec.WriteVarInt(payload, Http3Codec.ControlStream);
            Http3Codec.WriteFrame(payload, Http3Codec.SettingsFrame, ReadOnlySpan<byte>.Empty);
            using var goAwayPayload = new MemoryStream();
            Http3Codec.WriteVarInt(goAwayPayload, 0);
            Http3Codec.WriteFrame(payload, Http3Codec.GoAwayFrame, goAwayPayload.ToArray());
            await control.WriteAsync(payload.ToArray(), completeWrites: false, cancellationToken).ConfigureAwait(false);
            return control;
        }
        catch
        {
            await control.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task DrainAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        await using var ownedStream = stream.ConfigureAwait(false);
        var buffer = new byte[256];
        while (await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
        {
        }
    }

    private static async ValueTask<Http3FrameReadResult> ReadFrameAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        var type = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!type.Success)
        {
            return Http3FrameReadResult.End;
        }

        var length = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!length.Success || length.Value < 0 || length.Value > 1024 * 1024)
        {
            throw new IOException("Invalid HTTP/3 frame length.");
        }

        var payload = length.Value == 0 ? [] : await ReadExactAsync(stream, (int)length.Value, cancellationToken).ConfigureAwait(false);
        return new Http3FrameReadResult(false, type.Value, payload);
    }

    private static async ValueTask<Http3VarIntReadResult> ReadVarIntAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        var first = await ReadExactAsync(stream, 1, cancellationToken, allowEnd: true).ConfigureAwait(false);
        if (first.Length == 0)
        {
            return Http3VarIntReadResult.Failure;
        }

        var length = 1 << (first[0] >> 6);
        var value = first[0] & 0x3f;
        if (length == 1)
        {
            return new Http3VarIntReadResult(true, value);
        }

        var rest = await ReadExactAsync(stream, length - 1, cancellationToken).ConfigureAwait(false);
        foreach (var next in rest)
        {
            value = (value << 8) | next;
        }

        return new Http3VarIntReadResult(true, value);
    }

    private static async ValueTask<byte[]> ReadExactAsync(QuicStream stream, int length, CancellationToken cancellationToken, bool allowEnd = false)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return allowEnd && offset == 0 ? [] : throw new IOException("Connection closed while reading HTTP/3 data.");
            }

            offset += read;
        }

        return buffer;
    }

    private static ProxyOptions OptionsWithUpstream(string scheme, string protocol)
    {
        return new ProxyOptions
        {
            Listeners = [new ListenerOptions
            {
                Name = "main",
                Address = "127.0.0.1",
                Port = 8080
            }

            ],
            Routes = [new ProxyRouteOptions
            {
                Name = "proxy",
                Host = "*",
                PathPrefix = "/",
                Upstreams = [new UpstreamOptions
                {
                    Name = "bad",
                    Scheme = scheme,
                    Protocol = protocol,
                    Address = "127.0.0.1",
                    Port = 5000
                }

                ]
            }

            ]
        };
    }

    private static RuntimeRoute Route(IReadOnlyList<RuntimeUpstream> upstreams)
    {
        return new RuntimeRoute("test", "*", "/", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(true, "/health", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), 1, 1), upstreams, new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", ""), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), RuntimeCachePolicy.Disabled, new RuntimeRouteResolvedOptions(100L * 1024 * 1024, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true));
    }

    private static RuntimeTimeouts HealthCheckTimeouts(TimeSpan timeout)
    {
        return new RuntimeTimeouts(timeout, timeout, timeout, timeout, timeout, timeout, timeout, timeout, timeout, timeout);
    }

    private static RuntimeUpstream Upstream(int port, string protocol, bool validateCertificate = false, string sniHost = "upstream.test")
    {
        return new RuntimeUpstream("test", $"upstream-{port}", "https", protocol, "127.0.0.1", port, 1, new RuntimeUpstreamTlsOptions(validateCertificate, sniHost));
    }

    private static UpstreamHealthCheckTarget Target(RuntimeRoute route, RuntimeUpstream upstream)
    {
        return new UpstreamHealthCheckTarget(route.Name, upstream.Name, upstream.Endpoint, upstream.Identity, UpstreamTransportEndpointMapper.FromUpstream(upstream), route.HealthCheck.Path, route.HealthCheck.Interval, route.HealthCheck.Timeout, route.HealthCheck.HealthyThreshold, route.HealthCheck.UnhealthyThreshold);
    }

    private static ProxyConfigurationLoader CreateLoader(string dataDirectory)
    {
        var provider = new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory });
        return new ProxyConfigurationLoader(provider, new ProxyDataDirectoryBootstrapper(provider), new SiteConfigurationParser(), new Mk8.Drava.Application.INF.Configuration.ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, TimeProvider.System);
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

    private static async Task<string> SendSingleRequestAsync(int proxyPort, string request, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort, cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();
        await using var streamDisposal = stream.ConfigureAwait(false);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
        return await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static X509Certificate2 CreateServerCertificate(string subjectName)
    {
        var pfxBytes = TestCertificates.CreateSelfSignedPfxBytes(subjectName);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, ReadOnlySpan<char>.Empty, X509KeyStorageFlags.UserKeySet);
    }

    private static int GetFreeTcpPort() => TestPortAllocator.GetFreeTcpPort();
    private static int GetFreeUdpPort() => TestPortAllocator.GetFreeUdpPort();
    private sealed record ProxyScenarioResult(string ClientResponse, string SecondClientResponse, Http3UpstreamObservation Upstream, ProxyMetricsSnapshot Metrics);
    private sealed record ReusableProxyScenarioResult(IReadOnlyList<string> ClientResponses, ReusableHttp3UpstreamObservation Upstream, ProxyMetricsSnapshot Metrics)
    {
        public string FirstClientResponse => ClientResponses.Count > 0 ? ClientResponses[0] : "";
        public string SecondClientResponse => ClientResponses.Count > 1 ? ClientResponses[1] : "";
    }

    private sealed record Http3UpstreamObservation(IReadOnlyDictionary<string, string> RequestHeaders, byte[] RequestBody, string? Error);
    private sealed record ReusableHttp3UpstreamObservation(int ConnectionCount, IReadOnlyList<Http3UpstreamObservation> Requests);
    private readonly record struct Http3FrameReadResult(bool EndStream, long Type, ReadOnlyMemory<byte> Payload)
    {
        public static Http3FrameReadResult End { get; } = new(true, 0, ReadOnlyMemory<byte>.Empty);
    }

    private readonly record struct Http3VarIntReadResult(bool Success, long Value)
    {
        public static Http3VarIntReadResult Failure { get; } = new(false, 0);
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-upstream-h3-{Guid.NewGuid():N}");
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
            catch
            {
            }
        }
    }
}
