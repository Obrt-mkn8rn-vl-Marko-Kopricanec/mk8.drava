using System.Buffers.Binary;
using System.Net;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class UpstreamHttp2Tests
{
    private static readonly byte[] ClientPreface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
    public static async Task ExistingHttp1UpstreamProtocolRemainsDefaultAsync()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteSite(temp.Path, "home.json", 18080, 15000);
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        AssertEx.Equal(RuntimeUpstreamProtocol.Http1, ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Routes[0].Upstreams[0].Protocol);
    }

    public static void Http2UpstreamRequiresHttps()
    {
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, new ProxyOptions { Listeners = [new ListenerOptions { Name = "main", Address = "127.0.0.1", Port = 8080 }], Routes = [new ProxyRouteOptions { Name = "proxy", Host = "*", PathPrefix = "/", Upstreams = [new UpstreamOptions { Name = "bad", Scheme = "http", Protocol = RuntimeUpstreamProtocol.Http2, Address = "127.0.0.1", Port = 5000 }] }] });
        AssertEx.True(validation.Failed);
        AssertEx.True(AssertEx.NotNull(validation.Failures).Any(static failure => failure.Contains("HTTP/2 upstreams require scheme 'https'", StringComparison.Ordinal)));
    }

    public static void UnsupportedUpstreamProtocolIsRejected()
    {
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, new ProxyOptions { Listeners = [new ListenerOptions { Name = "main", Address = "127.0.0.1", Port = 8080 }], Routes = [new ProxyRouteOptions { Name = "proxy", Host = "*", PathPrefix = "/", Upstreams = [new UpstreamOptions { Name = "bad", Protocol = "spdy", Address = "127.0.0.1", Port = 5000 }] }] });
        AssertEx.True(validation.Failed);
        AssertEx.True(AssertEx.NotNull(validation.Failures).Any(static failure => failure.Contains("Protocol must be 'http1', 'http2', or 'http3'", StringComparison.Ordinal)));
    }

    public static void PoolKeyDiffersForHttp1AndHttp2()
    {
        var http1 = Upstream(5001, RuntimeUpstreamProtocol.Http1);
        var http2 = Upstream(5001, RuntimeUpstreamProtocol.Http2);
        AssertEx.False(string.Equals(UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(http1)), UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(http2)), StringComparison.Ordinal));
    }

    public static async Task UpstreamAlpnAdvertisesHttp2Async()
    {
        var port = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunSingleHttp2UpstreamAsync(port, 204, [], [], timeout.Token, readRequest: false);
        var upstream = Upstream(port, RuntimeUpstreamProtocol.Http2);
        using var transport = await new UpstreamConnectionFactory().ConnectAsync(UpstreamTransportEndpointMapper.FromUpstream(upstream), TimeSpan.FromSeconds(2), timeout.Token).ConfigureAwait(false);
        transport.Dispose();
        var observation = await serverTask.WaitAsync(timeout.Token).ConfigureAwait(false);
        AssertEx.Equal(SslApplicationProtocol.Http2, observation.NegotiatedProtocol);
    }

    public static async Task AlpnFailureDoesNotFallbackToHttp1Async()
    {
        var port = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunSingleHttp2UpstreamAsync(port, 204, [], [], timeout.Token, applicationProtocols: [SslApplicationProtocol.Http11], readRequest: false);
        var upstream = Upstream(port, RuntimeUpstreamProtocol.Http2);
        await AssertEx.ThrowsAsync<UpstreamTlsException>(async () =>
        {
            using var _ = await new UpstreamConnectionFactory().ConnectAsync(UpstreamTransportEndpointMapper.FromUpstream(upstream), TimeSpan.FromSeconds(2), timeout.Token).ConfigureAwait(false);
        }).ConfigureAwait(false);
        var observation = await serverTask.WaitAsync(timeout.Token).ConfigureAwait(false);
        AssertEx.False(observation.RequestHeaders.ContainsKey(":method"));
    }

    public static async Task Http2UpstreamProxyMapsHeadersQueryAndResponseAsync()
    {
        var result = await RunProxyScenarioAsync("/api/users?id=1", 200, [("content-length", "7"), ("x-upstream", "h2")], Encoding.ASCII.GetBytes("h2-body"), requestHeaders: "Connection: keep-alive\r\nKeep-Alive: timeout=5\r\n").ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.ClientResponse.EndsWith("h2-body", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("GET", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal("https", result.Upstream.RequestHeaders[":scheme"]);
        AssertEx.Equal("/api/users?id=1", result.Upstream.RequestHeaders[":path"]);
        AssertEx.False(result.Upstream.RequestHeaders.ContainsKey("connection"));
        AssertEx.False(result.Upstream.RequestHeaders.ContainsKey("keep-alive"));
    }

    public static async Task CacheWorksWithHttp2UpstreamAsync()
    {
        var result = await RunProxyScenarioAsync("/cache", 200, [("content-length", "8"), ("cache-control", "max-age=60")], Encoding.ASCII.GetBytes("cache-h2"), routeExtraJson: """
                  "cache": {
                    "enabled": true,
                    "maxEntryBytes": 4096,
                    "maxTotalBytes": 8192,
                    "defaultTtlSeconds": 60,
                    "respectOriginCacheControl": true
                  },
            """, sendSecondRequest: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("cache-h2", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.SecondClientResponse.Contains("cache-h2", StringComparison.Ordinal), result.SecondClientResponse);
        AssertEx.True(result.Metrics.UpstreamHttp2.Requests >= 1, result.Metrics.UpstreamHttp2.Requests.ToString());
    }

    public static async Task Http2UpstreamForwardsRequestBodyAsync()
    {
        var result = await RunProxyScenarioAsync("/submit", 201, [("content-length", "2")], Encoding.ASCII.GetBytes("ok"), method: "POST", requestBody: "hello world").ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("201 Created", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("POST", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal("11", result.Upstream.RequestHeaders["content-length"]);
        AssertEx.Equal("hello world", Encoding.ASCII.GetString(result.Upstream.RequestBody));
    }

    public static async Task Http2UpstreamEndsZeroLengthRequestBodyAsync()
    {
        var result = await RunProxyScenarioAsync("/empty", 204, [], [], method: "POST", requestBody: "", forceContentLength: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("204 No Content", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("POST", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal(0, result.Upstream.RequestBody.Length);
    }

    public static async Task Http2HealthCheckUsesH2AndRejectsWrongAlpnAsync()
    {
        var healthyPort = GetFreeTcpPort();
        var wrongAlpnPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var healthyServer = RunSingleHttp2UpstreamAsync(healthyPort, 204, [], [], timeout.Token);
        var wrongAlpnServer = RunSingleHttp2UpstreamAsync(wrongAlpnPort, 204, [], [], timeout.Token, applicationProtocols: [SslApplicationProtocol.Http11], readRequest: false);
        var client = new UpstreamHealthCheckClient(new UpstreamConnectionFactory(), new ProxyMetrics());
        var healthyUpstream = Upstream(healthyPort, RuntimeUpstreamProtocol.Http2);
        var wrongAlpnUpstream = Upstream(wrongAlpnPort, RuntimeUpstreamProtocol.Http2);
        var healthy = await client.CheckAsync(Target(Route([healthyUpstream]), healthyUpstream), timeout.Token).ConfigureAwait(false);
        var unhealthy = await client.CheckAsync(Target(Route([wrongAlpnUpstream]), wrongAlpnUpstream), timeout.Token).ConfigureAwait(false);
        var observation = await healthyServer.WaitAsync(timeout.Token).ConfigureAwait(false);
        _ = await wrongAlpnServer.WaitAsync(timeout.Token).ConfigureAwait(false);
        AssertEx.True(healthy.Healthy, healthy.Result);
        AssertEx.True(healthy.Result.Contains("HTTP/2 204", StringComparison.Ordinal), healthy.Result);
        AssertEx.Equal("GET", observation.RequestHeaders[":method"]);
        AssertEx.False(unhealthy.Healthy, unhealthy.Result);
    }

    public static async Task MetricsIncludeUpstreamHttp2CountersAsync()
    {
        var result = await RunProxyScenarioAsync("/metrics", 200, [("content-length", "2")], Encoding.ASCII.GetBytes("ok")).ConfigureAwait(false);
        AssertEx.True(result.Metrics.UpstreamHttp2.Requests >= 1, result.Metrics.UpstreamHttp2.Requests.ToString());
    }

    public static async Task Http2UpstreamCloseBeforeResponseHeadersReturnsSafeFailureAsync()
    {
        var result = await RunProxyScenarioAsync("/close-before-headers", 200, [("content-length", "2")], Encoding.ASCII.GetBytes("ok"), closeBeforeResponseHeaders: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("502 Bad Gateway", StringComparison.Ordinal) || result.ClientResponse.Contains("504 Gateway Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("GET", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal("/close-before-headers", result.Upstream.RequestHeaders[":path"]);
        AssertEx.True(result.Metrics.UpstreamForwarding.Failures >= 1, result.Metrics.UpstreamForwarding.Failures.ToString());
    }

    public static async Task Http2UpstreamCloseAfterResponseHeadersDoesNotRetryAfterHeadersAreSentAsync()
    {
        var result = await RunProxyScenarioAsync("/close-after-headers", 200, [("content-length", "8")], Encoding.ASCII.GetBytes("ignored"), routeExtraJson: RetryJson(), closeAfterResponseHeaders: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("GET", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal(0L, result.Metrics.Resilience.RetryAttempts);
    }

    public static async Task Http2StreamingPostBodyIsNotRetriedAfterUpstreamFailureAsync()
    {
        var result = await RunProxyScenarioAsync("/post-failure", 200, [("content-length", "2")], Encoding.ASCII.GetBytes("ok"), routeExtraJson: RetryJson(), method: "POST", requestBody: "streamed-h2", closeBeforeResponseHeaders: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("502 Bad Gateway", StringComparison.Ordinal) || result.ClientResponse.Contains("504 Gateway Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("POST", result.Upstream.RequestHeaders[":method"]);
        AssertEx.Equal("streamed-h2", Encoding.ASCII.GetString(result.Upstream.RequestBody));
        AssertEx.Equal(0L, result.Metrics.Resilience.RetryAttempts);
        AssertEx.True(result.Metrics.Resilience.RetrySkipped.Any(static skipped => skipped.Reason is "method" or "request_body"));
    }

    private static async Task<ProxyScenarioResult> RunProxyScenarioAsync(string target, int statusCode, IReadOnlyList<(string Name, string Value)> responseHeaders, byte[] responseBody, string requestHeaders = "", string routeExtraJson = "", bool sendSecondRequest = false, string method = "GET", string requestBody = "", bool forceContentLength = false, bool closeBeforeResponseHeaders = false, bool closeAfterResponseHeaders = false)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteHttpProxyToHttp2UpstreamSite(temp.Path, proxyPort, upstreamPort, routeExtraJson);
        var upstreamTask = RunSingleHttp2UpstreamAsync(upstreamPort, statusCode, responseHeaders, responseBody, timeout.Token, closeBeforeResponseHeaders: closeBeforeResponseHeaders, closeAfterResponseHeaders: closeAfterResponseHeaders);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var requestBodyBytes = Encoding.ASCII.GetBytes(requestBody);
            var contentLengthHeader = requestBodyBytes.Length > 0 || forceContentLength ? $"Content-Length: {requestBodyBytes.Length}\r\n" : "";
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

    private static void WriteHttpProxyToHttp2UpstreamSite(string dataDirectory, int proxyPort, int upstreamPort, string routeExtraJson)
    {
        ConfigurationTests.WriteCustomSite(dataDirectory, "upstream-h2.json", $$"""
            {
              "name": "upstream-h2",
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
                      "name": "h2-upstream",
                      "scheme": "https",
                      "protocol": "http2",
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
            """);
    }

    private static async Task<Http2UpstreamObservation> RunSingleHttp2UpstreamAsync(int port, int statusCode, IReadOnlyList<(string Name, string Value)> responseHeaders, byte[] responseBody, CancellationToken cancellationToken, IReadOnlyList<SslApplicationProtocol>? applicationProtocols = null, bool readRequest = true, bool closeBeforeResponseHeaders = false, bool closeAfterResponseHeaders = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        using var certificate = CreateServerCertificate("upstream.test");
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await using var streamDisposal = stream.ConfigureAwait(false);
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, ApplicationProtocols = applicationProtocols is null ? [SslApplicationProtocol.Http2] : applicationProtocols.ToList() }, cancellationToken).ConfigureAwait(false);
            if (!readRequest || stream.NegotiatedApplicationProtocol != SslApplicationProtocol.Http2)
            {
                return new Http2UpstreamObservation(stream.NegotiatedApplicationProtocol, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], null);
            }

            var preface = await ReadExactAsync(stream, ClientPreface.Length, cancellationToken).ConfigureAwait(false);
            if (!preface.AsSpan().SequenceEqual(ClientPreface))
            {
                return new Http2UpstreamObservation(stream.NegotiatedApplicationProtocol, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], "bad preface");
            }

            var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var requestBody = new MemoryStream();
            var streamId = 1;
            await WriteFrameAsync(stream, Http2TestFrameType.Settings, 0, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            while (true)
            {
                var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                if (frame.Type == Http2TestFrameType.Settings)
                {
                    if ((frame.Flags & Http2TestFlags.Ack) == 0)
                    {
                        await WriteFrameAsync(stream, Http2TestFrameType.Settings, Http2TestFlags.Ack, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (frame.Type == Http2TestFrameType.Headers)
                {
                    streamId = frame.StreamId;
                    foreach (var header in DecodeHeaders(frame.Payload.ToArray()))
                    {
                        requestHeaders[header.Name] = header.Value;
                    }

                    if ((frame.Flags & Http2TestFlags.EndStream) != 0)
                    {
                        break;
                    }

                    continue;
                }

                if (frame.Type == Http2TestFrameType.Data)
                {
                    requestBody.Write(frame.Payload.Span);
                    if ((frame.Flags & Http2TestFlags.EndStream) != 0)
                    {
                        break;
                    }
                }
            }

            var observation = new Http2UpstreamObservation(stream.NegotiatedApplicationProtocol, requestHeaders, requestBody.ToArray(), null);
            if (closeBeforeResponseHeaders)
            {
                return observation;
            }

            var block = EncodeResponseHeaders(statusCode, responseHeaders);
            await WriteFrameAsync(stream, Http2TestFrameType.Headers, responseBody.Length == 0 ? (byte)(Http2TestFlags.EndHeaders | Http2TestFlags.EndStream) : Http2TestFlags.EndHeaders, streamId, block, cancellationToken).ConfigureAwait(false);
            if (closeAfterResponseHeaders)
            {
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                return observation;
            }

            if (responseBody.Length > 0)
            {
                await WriteFrameAsync(stream, Http2TestFrameType.Data, Http2TestFlags.EndStream, streamId, responseBody, cancellationToken).ConfigureAwait(false);
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            var closeBuffer = new byte[1];
            try
            {
                _ = await stream.ReadAsync(closeBuffer, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }

            return observation;
        }
        catch (Exception exception)when (exception is AuthenticationException or IOException)
        {
            return new Http2UpstreamObservation(default, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], exception.GetType().Name);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static RuntimeRoute Route(IReadOnlyList<RuntimeUpstream> upstreams)
    {
        return new RuntimeRoute("test", "*", "/", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(true, "/health", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), 1, 1), upstreams, new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", ""), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), RuntimeCachePolicy.Disabled, new RuntimeRouteResolvedOptions(100L * 1024 * 1024, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true));
    }

    private static RuntimeUpstream Upstream(int port, string protocol)
    {
        return new RuntimeUpstream("test", $"upstream-{port}", "https", protocol, "127.0.0.1", port, 1, new RuntimeUpstreamTlsOptions(false, "upstream.test"));
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

            buffer.Write(chunk, 0, bytesRead);
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static async Task<Http2TestFrame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        return await Http2TestFrames.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        return Http2TestFrames.ReadExactAsync(stream, length, cancellationToken);
    }

    private static async Task WriteFrameAsync(Stream stream, Http2TestFrameType type, byte flags, int streamId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await Http2TestFrames.WriteAsync(stream, type, flags, streamId, payload, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] EncodeResponseHeaders(int statusCode, IReadOnlyList<(string Name, string Value)> headers)
    {
        using var memory = new MemoryStream();
        var indexed = statusCode switch
        {
            200 => 8,
            204 => 9,
            400 => 12,
            404 => 13,
            500 => 14,
            _ => 0
        };
        if (indexed > 0)
        {
            WriteInteger(memory, 0x80, 7, indexed);
        }
        else
        {
            WriteLiteralWithIndexedName(memory, 8, statusCode.ToString());
        }

        foreach (var header in headers)
        {
            var nameIndex = StaticNameIndex(header.Name);
            if (nameIndex == 0)
            {
                memory.WriteByte(0);
                WriteString(memory, header.Name.ToLowerInvariant());
                WriteString(memory, header.Value);
                continue;
            }

            WriteLiteralWithIndexedName(memory, nameIndex, header.Value);
        }

        return memory.ToArray();
    }

    private static IReadOnlyList<(string Name, string Value)> DecodeHeaders(byte[] block)
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

    private static void WriteLiteralWithIndexedName(Stream stream, int nameIndex, string value)
    {
        WriteInteger(stream, 0, 4, nameIndex);
        WriteString(stream, value);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteInteger(stream, 0, 7, bytes.Length);
        stream.Write(bytes);
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

    private static int StaticNameIndex(string name)
    {
        for (var index = 1; index < StaticTable.Length; index++)
        {
            if (string.Equals(StaticTable[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private static X509Certificate2 CreateServerCertificate(string subjectName)
    {
        var pfxBytes = TestCertificates.CreateSelfSignedPfxBytes(subjectName);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, ReadOnlySpan<char>.Empty, X509KeyStorageFlags.UserKeySet);
    }

    private static int GetFreeTcpPort() => TestPortAllocator.GetFreeTcpPort();
    private static readonly (string Name, string Value)[] StaticTable = [("", ""), (":authority", ""), (":method", "GET"), (":method", "POST"), (":path", "/"), (":path", "/index.html"), (":scheme", "http"), (":scheme", "https"), (":status", "200"), (":status", "204"), (":status", "206"), (":status", "304"), (":status", "400"), (":status", "404"), (":status", "500"), ("accept-charset", ""), ("accept-encoding", "gzip, deflate"), ("accept-language", ""), ("accept-ranges", ""), ("accept", ""), ("access-control-allow-origin", ""), ("age", ""), ("allow", ""), ("authorization", ""), ("cache-control", ""), ("content-disposition", ""), ("content-encoding", ""), ("content-language", ""), ("content-length", ""), ("content-location", ""), ("content-range", ""), ("content-type", ""), ("cookie", ""), ("date", ""), ("etag", ""), ("expect", ""), ("expires", ""), ("from", ""), ("host", ""), ("if-match", ""), ("if-modified-since", ""), ("if-none-match", ""), ("if-range", ""), ("if-unmodified-since", ""), ("last-modified", ""), ("link", ""), ("location", ""), ("max-forwards", ""), ("proxy-authenticate", ""), ("proxy-authorization", ""), ("range", ""), ("referer", ""), ("refresh", ""), ("retry-after", ""), ("server", ""), ("set-cookie", ""), ("strict-transport-security", ""), ("transfer-encoding", ""), ("user-agent", ""), ("vary", ""), ("via", ""), ("www-authenticate", "")];
    private sealed record ProxyScenarioResult(string ClientResponse, string SecondClientResponse, Http2UpstreamObservation Upstream, ProxyMetricsSnapshot Metrics);
    private sealed record Http2UpstreamObservation(SslApplicationProtocol NegotiatedProtocol, IReadOnlyDictionary<string, string> RequestHeaders, byte[] RequestBody, string? Error);
    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-upstream-h2-{Guid.NewGuid():N}");
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
