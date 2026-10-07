using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ProxyIntegrationTests
{
    public static async Task ProxiesSingleGetToUpstreamAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-integration-{Guid.NewGuid():N}");
        ConfigurationTests.WriteSite(dataDirectory, "smoke.json", proxyPort, upstreamPort);
        var upstreamTask = RunSingleResponseUpstreamAsync(upstreamPort, timeout.Token);
        try
        {
            using var host = Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
            {
                builder.Sources.Clear();
                builder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Mdrava:DataDirectory"] = dataDirectory });
            }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) =>
            {
                services.AddProxyDataPlane(context.Configuration);
            }).Build();
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            await WaitForProxyRuntimeAsync(host, timeout.Token).ConfigureAwait(false);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                var requestBytes = Encoding.ASCII.GetBytes("GET /smoke HTTP/1.1\r\nHost: smoke.test\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(requestBytes, timeout.Token).ConfigureAwait(false);
                var responseText = await ReadToEndAsync(stream, timeout.Token).ConfigureAwait(false);
                AssertEx.True(responseText.Contains("200 OK", StringComparison.Ordinal), responseText);
                AssertEx.True(responseText.EndsWith("proxied", StringComparison.Ordinal), responseText);
                var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertEx.True(upstreamRequest.StartsWith("GET /smoke HTTP/1.1", StringComparison.Ordinal), upstreamRequest);
                AssertEx.True(upstreamRequest.Contains("Connection: keep-alive", StringComparison.OrdinalIgnoreCase), upstreamRequest);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    public static async Task ProxiesFixedLengthRequestAndResponseAsync()
    {
        var request = "POST /submit HTTP/1.1\r\nHost: fixed.test\r\nContent-Length: 11\r\n\r\nhello world";
        var upstreamResponse = "HTTP/1.1 201 Created\r\nContent-Length: 7\r\nContent-Type: text/plain\r\n\r\ncreated";
        var result = await RunProxyScenarioAsync(request, upstreamResponse).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("201 Created", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.ClientResponse.EndsWith("created", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.UpstreamRequest.Contains("Content-Length: 11", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.EndsWith("hello world", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task ProxiesChunkedRequestAndResponseAsync()
    {
        var request = "POST /chunks HTTP/1.1\r\nHost: chunk.test\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n6\r\n world\r\n0\r\nX-Trailer: ok\r\n\r\n";
        var upstreamResponse = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n6\r\n world\r\n0\r\n\r\n";
        var result = await RunProxyScenarioAsync(request, upstreamResponse).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
        AssertEx.True(result.ClientResponse.EndsWith("0\r\n\r\n", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.UpstreamRequest.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Trailer: ok", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task AcceptsChunkExtensionsAndForwardsChunkedBodyAsync()
    {
        var result = await RunProxyScenarioAsync("POST /chunk-ext HTTP/1.1\r\nHost: chunk.test\r\nTransfer-Encoding: chunked\r\n\r\n5;foo=bar\r\nhello\r\n0\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok").ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.UpstreamRequest.Contains("5;foo=bar\r\nhello", StringComparison.Ordinal), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.EndsWith("0\r\n\r\n", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task ForwardsDeclaredChunkedRequestTrailerAsync()
    {
        var result = await RunProxyScenarioAsync("POST /trailers HTTP/1.1\r\nHost: chunk.test\r\nTransfer-Encoding: chunked\r\nTrailer: X-Trailer\r\n\r\n5\r\nhello\r\n0\r\nX-Trailer: ok\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok").ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.UpstreamRequest.Contains("Trailer: X-Trailer", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Trailer: ok", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task DoesNotRelayHeadResponseBodyAsync()
    {
        var result = await RunProxyScenarioAsync("HEAD /head HTTP/1.1\r\nHost: head.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.False(result.ClientResponse.EndsWith("hello", StringComparison.Ordinal), result.ClientResponse);
    }

    public static async Task ProxiesNoContentWithoutBodyAsync()
    {
        var result = await RunProxyScenarioAsync("GET /empty HTTP/1.1\r\nHost: empty.test\r\n\r\n", "HTTP/1.1 204 No Content\r\nContent-Length: 5\r\n\r\nhello", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("204 No Content", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.False(result.ClientResponse.EndsWith("hello", StringComparison.Ordinal), result.ClientResponse);
    }

    public static async Task ProxiesNotModifiedWithoutBodyAsync()
    {
        var result = await RunProxyScenarioAsync("GET /cached HTTP/1.1\r\nHost: cache.test\r\n\r\n", "HTTP/1.1 304 Not Modified\r\nContent-Length: 5\r\n\r\nhello", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("304 Not Modified", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.False(result.ClientResponse.EndsWith("hello", StringComparison.Ordinal), result.ClientResponse);
    }

    public static async Task RejectsInvalidRequestFramingAsync()
    {
        var result = await RunProxyScenarioAsync("POST /bad HTTP/1.1\r\nHost: bad.test\r\nContent-Length: 1\r\nTransfer-Encoding: chunked\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("400 Bad Request", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("", result.UpstreamRequest);
    }

    public static async Task RejectsMalformedChunkedRequestBodyAsync()
    {
        var result = await RunProxyScenarioAsync("POST /bad-chunk HTTP/1.1\r\nHost: bad.test\r\nTransfer-Encoding: chunked\r\n\r\nZ\r\nbad\r\n0\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", readBodyFromUpstreamRequest: false, expectUpstreamConnection: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("400 Bad Request", StringComparison.Ordinal), result.ClientResponse);
    }

    public static async Task FiltersHopByHopRequestHeadersAsync()
    {
        var result = await RunProxyScenarioAsync("GET /headers HTTP/1.1\r\nHost: header.test\r\nConnection: x-private, close\r\nX-Private: secret\r\nKeep-Alive: timeout=5\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.False(result.UpstreamRequest.Contains("X-Private", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.False(result.UpstreamRequest.Contains("\r\nKeep-Alive:", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
    }

    public static async Task PreservesHostHeaderAsync()
    {
        var result = await RunProxyScenarioAsync("GET /host HTTP/1.1\r\nHost: original.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.Contains("Host: original.test", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
    }

    public static async Task ResponseIncludesGeneratedRequestIdAsync()
    {
        var result = await RunProxyScenarioAsync("GET /id HTTP/1.1\r\nHost: id.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("\r\nX-Request-Id: mdr-", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
        AssertEx.Equal(1, result.Diagnostics.Count);
        AssertEx.True(result.Diagnostics[0].RequestId.StartsWith("mdr-", StringComparison.Ordinal));
    }

    public static async Task ExternalRequestIdIsPreservedInDiagnosticsAsync()
    {
        var result = await RunProxyScenarioAsync("GET /external HTTP/1.1\r\nHost: id.test\r\nX-Request-Id: client-123\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.Equal("client-123", result.Diagnostics[0].ExternalRequestId);
        AssertEx.True(result.Diagnostics[0].RequestId.StartsWith("mdr-", StringComparison.Ordinal));
        AssertEx.False(string.Equals(result.Diagnostics[0].RequestId, result.Diagnostics[0].ExternalRequestId, StringComparison.Ordinal));
    }

    public static async Task SuccessfulRequestProducesDiagnosticRouteAndUpstreamAsync()
    {
        var result = await RunProxyScenarioAsync("GET /diag HTTP/1.1\r\nHost: diag.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        var diagnostic = result.Diagnostics[0];
        AssertEx.Equal("scenario", diagnostic.RouteName);
        AssertEx.Equal("local-test", diagnostic.UpstreamName);
        AssertEx.Equal(200, diagnostic.ResponseStatusCode);
        AssertEx.Equal("None", diagnostic.FailureKind);
    }

    public static async Task UpstreamConnectFailureProducesDiagnosticClassificationAsync()
    {
        var result = await RunProxyScenarioAsync("GET /unavailable HTTP/1.1\r\nHost: upstream.test\r\n\r\n", "", expectUpstreamConnection: false).ConfigureAwait(false);
        AssertEx.Equal("UpstreamConnectFailed", result.Diagnostics[0].FailureKind);
        AssertEx.Equal(502, result.Diagnostics[0].ResponseStatusCode);
        AssertEx.Equal(1L, result.Metrics.RequestClassifications.FailuresByKind["UpstreamConnectFailed"]);
    }

    public static async Task AccessLoggingCanBeDisabledWhileDiagnosticsRemainEnabledAsync()
    {
        var result = await RunProxyScenarioAsync("GET /quiet HTTP/1.1\r\nHost: quiet.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false, accessLogEnabled: false).ConfigureAwait(false);
        AssertEx.Equal(1, result.Diagnostics.Count);
        AssertEx.Equal(0L, result.Metrics.Diagnostics.AccessLogsEmitted);
    }

    public static async Task HttpToHttpsRedirectPreservesPathAndQueryAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "httpsRedirect": {
                    "enabled": true,
                    "httpsPort": 9443
                  },
                """), "GET /app/users?id=1 HTTP/1.1\r\nHost: example.test\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("308 Permanent Redirect", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.ClientResponse.Contains("Location: https://example.test:9443/app/users?id=1", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
        AssertEx.Equal("", result.UpstreamRequest);
    }

    public static async Task CanonicalHostRedirectWorksAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "canonicalHost": {
                    "targetHost": "www.example.test"
                  },
                """), "GET /docs?page=2 HTTP/1.1\r\nHost: example.test\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("308 Permanent Redirect", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.ClientResponse.Contains("Location: http://www.example.test/docs?page=2", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
    }

    public static async Task CanonicalHostRedirectDoesNotLoopAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "canonicalHost": {
                    "targetHost": "example.test"
                  },
                """), "GET /docs HTTP/1.1\r\nHost: example.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.False(result.ClientResponse.Contains("Location:", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /docs HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task ForwardedHeadersGeneratedForUntrustedDirectClientAsync()
    {
        var result = await RunProxyScenarioAsync("GET /forwarded HTTP/1.1\r\nHost: forward.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-For: 127.0.0.1", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-Host: forward.test", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-Proto: http", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-Port:", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("Forwarded:", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
    }

    public static async Task TrustedProxyAcceptsPriorForwardedChainAsync()
    {
        var result = await RunCustomProxyScenarioAsync(SiteWithSingleProxyRoute, "GET /trusted HTTP/1.1\r\nHost: trusted.test\r\nX-Forwarded-For: 203.0.113.10\r\nX-Forwarded-Proto: https\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false, configureOperational: dataDirectory => ConfigurationTests.WriteOperationalConfig(dataDirectory, trustedProxies: ["127.0.0.1"])).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-For: 203.0.113.10, 127.0.0.1", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-Proto: https", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.Equal("203.0.113.10", result.Diagnostics[0].ClientEndpoint);
    }

    public static async Task MalformedTrustedForwardedHeadersAreSanitizedBeforeUpstreamAsync()
    {
        var bad = (char)1;
        var result = await RunCustomProxyScenarioAsync(SiteWithSingleProxyRoute, $"GET /forwarded HTTP/1.1\r\nHost: forwarded.test\r\nX-Forwarded-For: bad{bad}value\r\nX-Forwarded-Host: bad{bad}host\r\nX-Forwarded-Proto: ftp{bad}\r\nConnection: close\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false, configureOperational: dataDirectory => ConfigurationTests.WriteOperationalConfig(dataDirectory, trustedProxies: ["127.0.0.1"])).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-For: 127.0.0.1", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-Host: forwarded.test", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-Proto: http", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.False(result.UpstreamRequest.Contains("bad", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
    }

    public static async Task UntrustedClientForwardedHeadersAreStrippedAndReplacedAsync()
    {
        var result = await RunProxyScenarioAsync("GET /untrusted HTTP/1.1\r\nHost: untrusted.test\r\nX-Forwarded-For: 203.0.113.10\r\nX-Forwarded-Proto: https\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-For: 127.0.0.1", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.False(result.UpstreamRequest.Contains("203.0.113.10", StringComparison.Ordinal), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Forwarded-Proto: http", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
    }

    public static async Task RequestHeaderSetAndRemoveRulesApplyUpstreamAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "headerPolicy": {
                    "removeRequestHeaders": [ "X-Remove" ],
                    "setRequestHeaders": [
                      {
                        "name": "X-Set",
                        "value": "yes"
                      }
                    ]
                  },
                """), "GET /headers HTTP/1.1\r\nHost: headers.test\r\nX-Remove: old\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.False(result.UpstreamRequest.Contains("X-Remove:", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
        AssertEx.True(result.UpstreamRequest.Contains("X-Set: yes", StringComparison.OrdinalIgnoreCase), result.UpstreamRequest);
    }

    public static async Task ResponseHeaderSetAndRemoveRulesApplyDownstreamAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "headerPolicy": {
                    "removeResponseHeaders": [ "X-Remove" ],
                    "setResponseHeaders": [
                      {
                        "name": "X-Set",
                        "value": "yes"
                      }
                    ]
                  },
                """), "GET /headers HTTP/1.1\r\nHost: headers.test\r\n\r\n", "HTTP/1.1 200 OK\r\nX-Remove: old\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.False(result.ClientResponse.Contains("X-Remove:", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
        AssertEx.True(result.ClientResponse.Contains("X-Set: yes", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
    }

    public static async Task PathPrefixStrippingPreservesQueryStringAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "pathRewrite": {
                    "stripPrefix": "/public"
                  },
                """), "GET /public/api/users?id=1 HTTP/1.1\r\nHost: rewrite.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /api/users?id=1 HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task PathPrefixReplacementWorksAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "pathRewrite": {
                    "replacePrefix": "/v1",
                    "replacement": "/api"
                  },
                """), "GET /v1/users?active=true HTTP/1.1\r\nHost: rewrite.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /api/users?active=true HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task PathRewriteNoMatchForwardsOriginalTargetAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "pathRewrite": {
                    "stripPrefix": "/public"
                  },
                """), "GET /private?id=1 HTTP/1.1\r\nHost: rewrite.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /private?id=1 HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task RedirectRouteReturnsConfiguredRedirectAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, _) => SiteWithRoutes(proxyPort, """
                    {
                      "name": "old",
                      "pathPrefix": "/old",
                      "action": "redirect",
                      "redirect": {
                        "statusCode": 307,
                        "targetPath": "/new",
                        "preserveQuery": true
                      }
                    }
                """), "GET /old?id=1 HTTP/1.1\r\nHost: redirect.test\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("307 Temporary Redirect", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.ClientResponse.Contains("Location: /new?id=1", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
        AssertEx.Equal("", result.UpstreamRequest);
    }

    public static async Task StaticResponseRouteReturnsConfiguredResponseAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, _) => SiteWithRoutes(proxyPort, """
                    {
                      "name": "gone",
                      "pathPrefix": "/gone",
                      "action": "staticResponse",
                      "staticResponse": {
                        "statusCode": 410,
                        "contentType": "text/plain",
                        "body": "gone"
                      }
                    }
                """), "GET /gone HTTP/1.1\r\nHost: static.test\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("410 Gone", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.ClientResponse.Contains("Content-Type: text/plain", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
        AssertEx.True(result.ClientResponse.EndsWith("gone", StringComparison.Ordinal), result.ClientResponse);
    }

    public static async Task MaintenanceModeReturns503AndDoesNotContactUpstreamAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "maintenance": {
                    "enabled": true,
                    "retryAfterSeconds": 120,
                    "contentType": "text/plain",
                    "body": "maintenance"
                  },
                """), "GET / HTTP/1.1\r\nHost: maintenance.test\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("503 Service Unavailable", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.ClientResponse.Contains("Retry-After: 120", StringComparison.OrdinalIgnoreCase), result.ClientResponse);
        AssertEx.True(result.ClientResponse.EndsWith("maintenance", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("", result.UpstreamRequest);
    }

    public static async Task PerRouteBodySizeOverrideWorksAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "overrides": {
                    "maxRequestBodyBytes": 10
                  },
                """), "POST /body HTTP/1.1\r\nHost: body.test\r\nContent-Length: 6\r\n\r\nabcdef", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", configureOperational: dataDirectory => ConfigurationTests.WriteOperationalConfig(dataDirectory, maxRequestBodyBytes: 5)).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.UpstreamRequest.EndsWith("abcdef", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task PerRouteAccessLogDisableIsReflectedInDiagnosticsAsync()
    {
        var result = await RunCustomProxyScenarioAsync((proxyPort, upstreamPort) => SiteWithSingleProxyRoute(proxyPort, upstreamPort, """
                  "overrides": {
                    "accessLogEnabled": false
                  },
                """), "GET /quiet HTTP/1.1\r\nHost: quiet-route.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", readBodyFromUpstreamRequest: false).ConfigureAwait(false);
        AssertEx.Equal(1, result.Diagnostics.Count);
        AssertEx.Equal(0L, result.Metrics.Diagnostics.AccessLogsEmitted);
        AssertEx.Equal("scenario", result.Diagnostics[0].RouteName);
    }

    public static async Task NoMatchingRouteProducesDiagnosticClassificationAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-no-route-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteSite(dataDirectory, "specific.json", proxyPort, upstreamPort);
            var sitePath = Path.Combine(dataDirectory, "config", "sites", "specific.json");
            File.WriteAllText(sitePath, File.ReadAllText(sitePath).Replace("\"host\": \"*\"", "\"host\": \"expected.test\"", StringComparison.Ordinal));
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            await WaitForProxyRuntimeAsync(host, timeout.Token).ConfigureAwait(false);
            try
            {
                var response = await SendSingleRequestAsync(proxyPort, "GET /missing HTTP/1.1\r\nHost: other.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
                AssertEx.True(response.Contains("404 Not Found", StringComparison.Ordinal), response);
                var status = ActivatorUtilities.CreateInstance<ProxyStatusController>(host.Services).Get();
                AssertEx.Equal(1, status.ConfigVersion);
                AssertEx.Equal(1L, status.Metrics.RequestFailuresByKind["NoMatchingRoute"]);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var diagnostic = host.Services.GetRequiredService<RecentRequestDiagnosticsStore>().Recent(10)[0];
            AssertEx.Equal("NoMatchingRoute", diagnostic.FailureKind);
            AssertEx.Equal(404, diagnostic.ResponseStatusCode);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task FailedReloadWhileProxyActivePreservesOldSnapshotAndTrafficAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-active-reload-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteSite(dataDirectory, "active.json", proxyPort, upstreamPort);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            var firstUpstream = RunScenarioUpstreamAsync(upstreamPort, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 3\r\n\r\nold", readBody: false, sendResponse: true, timeout.Token);
            var firstResponse = await SendSingleRequestAsync(proxyPort, "GET /before HTTP/1.1\r\nHost: reload.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            await firstUpstream.WaitAsync(timeout.Token).ConfigureAwait(false);
            ConfigurationTests.WriteCustomSite(dataDirectory, "broken.json", "{ nope");
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var secondUpstream = RunScenarioUpstreamAsync(upstreamPort, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 3\r\n\r\nold", readBody: false, sendResponse: true, timeout.Token);
            var secondResponse = await SendSingleRequestAsync(proxyPort, "GET /after HTTP/1.1\r\nHost: reload.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            await secondUpstream.WaitAsync(timeout.Token).ConfigureAwait(false);
            var store = host.Services.GetRequiredService<IProxyConfigurationStore>();
            AssertEx.True(firstResponse.EndsWith("old", StringComparison.Ordinal), firstResponse);
            ProxyConfigurationReloadResultAssertions.Failed(reload);
            AssertEx.Equal(1, store.Snapshot.Version);
            AssertEx.Equal(upstreamPort, store.Snapshot.Routes[0].Upstreams[0].Port);
            AssertEx.True(secondResponse.EndsWith("old", StringComparison.Ordinal), secondResponse);
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task OversizedRequestHeadIsRejectedAsync()
    {
        var largeHeader = new string ('a', 1500);
        var result = await RunProxyScenarioAsync($"GET /large HTTP/1.1\r\nHost: large.test\r\nX-Large: {largeHeader}\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false, maxRequestHeadBytes: 1024).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("431 Request Header Fields Too Large", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal(1L, result.Metrics.Rejections.ParserLimitRejections);
    }

    public static async Task ExcessiveHeaderCountIsRejectedAsync()
    {
        var result = await RunProxyScenarioAsync("GET /headers HTTP/1.1\r\nHost: headers.test\r\nX-One: 1\r\nX-Two: 2\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false, maxHeaderCount: 1).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("431 Request Header Fields Too Large", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("ParserLimitExceeded", result.Diagnostics[0].FailureKind);
    }

    public static async Task ExcessiveHeaderLineIsRejectedAsync()
    {
        var result = await RunProxyScenarioAsync($"GET /line HTTP/1.1\r\nHost: line.test\r\nX-Long: {new string ('a', 96)}\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false, maxHeaderLineBytes: 64).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("431 Request Header Fields Too Large", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("ParserLimitExceeded", result.Diagnostics[0].FailureKind);
    }

    public static async Task ExcessiveRequestBodySizeIsRejectedAsync()
    {
        var result = await RunProxyScenarioAsync("POST /body HTTP/1.1\r\nHost: body.test\r\nContent-Length: 6\r\n\r\nabcdef", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false, maxRequestBodyBytes: 5).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("413 Payload Too Large", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("RequestPayloadTooLarge", result.Diagnostics[0].FailureKind);
    }

    public static async Task RequestBodyExactlyAtConfiguredMaxIsAcceptedAsync()
    {
        var result = await RunProxyScenarioAsync("POST /body-max HTTP/1.1\r\nHost: body.test\r\nContent-Length: 5\r\n\r\nabcde", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", maxRequestBodyBytes: 5).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.UpstreamRequest.EndsWith("abcde", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task ChunkedRequestBodyExactlyAtConfiguredMaxIsAcceptedAsync()
    {
        var result = await RunProxyScenarioAsync("POST /chunk-max HTTP/1.1\r\nHost: body.test\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", maxRequestBodyBytes: 5).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.UpstreamRequest.Contains("5\r\nhello\r\n0\r\n\r\n", StringComparison.Ordinal), result.UpstreamRequest);
    }

    public static async Task RequestBodyConfiguredMaxPlusOneIsRejectedAsync()
    {
        var result = await RunProxyScenarioAsync("POST /body-max HTTP/1.1\r\nHost: body.test\r\nContent-Length: 6\r\n\r\nabcdef", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", expectUpstreamConnection: false, maxRequestBodyBytes: 5).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("413 Payload Too Large", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("", result.UpstreamRequest);
        AssertEx.Equal("RequestPayloadTooLarge", result.Diagnostics[0].FailureKind);
    }

    public static async Task ChunkedRequestBodySizeIsRejectedAsync()
    {
        var result = await RunProxyScenarioAsync("POST /chunk-limit HTTP/1.1\r\nHost: body.test\r\nTransfer-Encoding: chunked\r\n\r\n6\r\nabcdef\r\n0\r\n\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", readBodyFromUpstreamRequest: false, maxRequestBodyBytes: 5).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("413 Payload Too Large", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal("RequestPayloadTooLarge", result.Diagnostics[0].FailureKind);
    }

    public static async Task PerIpRequestRateLimitIsEnforcedAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var upstreamStop = new CancellationTokenSource();
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-rate-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteSite(dataDirectory, "rate.json", proxyPort, upstreamPort);
            ConfigurationTests.WriteOperationalConfig(dataDirectory, requestsPerMinutePerIp: 1);
            var upstreamTask = RunReusableHttpUpstreamAsync(upstreamPort, "ok", upstreamStop.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            var first = await SendSingleRequestAsync(proxyPort, "GET /one HTTP/1.1\r\nHost: rate.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            var second = await SendSingleRequestAsync(proxyPort, "GET /two HTTP/1.1\r\nHost: rate.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await upstreamStop.CancelAsync().ConfigureAwait(false);
            await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            var diagnostic = host.Services.GetRequiredService<RecentRequestDiagnosticsStore>().Recent(10)[0];
            AssertEx.True(first.Contains("200 OK", StringComparison.Ordinal), first);
            AssertEx.True(second.Contains("429 Too Many Requests", StringComparison.Ordinal), second);
            AssertEx.Equal(1L, metrics.Rejections.RateLimitedRequests);
            AssertEx.Equal("RateLimited", diagnostic.FailureKind);
        }
        finally
        {
            await upstreamStop.CancelAsync().ConfigureAwait(false);
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task ConcurrentClientAdmissionLimitRejectsLimitPlusOneAndRecoversAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var upstreamStop = new CancellationTokenSource();
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-admission-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteSite(dataDirectory, "admission.json", proxyPort, upstreamPort);
            ConfigurationTests.WriteOperationalConfig(dataDirectory, clientRequestHeadTimeoutMs: 5000, maxActiveClientConnections: 1);
            var upstreamTask = RunReusableHttpUpstreamAsync(upstreamPort, "ok", upstreamStop.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            await WaitForProxyRuntimeAsync(host, timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>();
            using var heldClient = new TcpClient();
            await heldClient.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
            await WaitForMetricsAsync(metrics, static snapshot => snapshot.ClientConnections.Active == 1, timeout.Token).ConfigureAwait(false);
            using var rejectedClient = new TcpClient();
            await rejectedClient.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
            await WaitForMetricsAsync(metrics, static snapshot => snapshot.Rejections.ClientConnectionAdmissionRejections == 1, timeout.Token).ConfigureAwait(false);
            heldClient.Dispose();
            await WaitForMetricsAsync(metrics, static snapshot => snapshot.ClientConnections.Active == 0, timeout.Token).ConfigureAwait(false);
            var response = await SendSingleRequestAsync(proxyPort, "GET /after-admission HTTP/1.1\r\nHost: admission.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await upstreamStop.CancelAsync().ConfigureAwait(false);
            var upstreamRequests = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var snapshot = metrics.Snapshot();
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            AssertEx.True(response.EndsWith("ok", StringComparison.Ordinal), response);
            AssertEx.Equal(1L, snapshot.Rejections.ClientConnectionAdmissionRejections);
            AssertEx.Equal(0L, snapshot.ClientConnections.Active);
            AssertEx.Equal(1, upstreamRequests);
        }
        finally
        {
            await upstreamStop.CancelAsync().ConfigureAwait(false);
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task ConcurrentPerIpRateLimitAllowsOnlyConfiguredBoundaryAsync()
    {
        var proxyPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-rate-concurrent-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteCustomSite(dataDirectory, "rate.json", $$"""
                {
                  "name": "rate",
                  "listeners": [
                    {
                      "name": "main",
                      "address": "127.0.0.1",
                      "port": {{proxyPort}}
                    }
                  ],
                  "host": "rate.test",
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
                """);
            ConfigurationTests.WriteOperationalConfig(dataDirectory, requestsPerMinutePerIp: 2);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            await WaitForProxyRuntimeAsync(host, timeout.Token).ConfigureAwait(false);
            var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => SendSingleRequestAsync(proxyPort, $"GET /rate-{index} HTTP/1.1\r\nHost: rate.test\r\nConnection: close\r\n\r\n", timeout.Token))).ConfigureAwait(false);
            await WaitForMetricsAsync(host.Services.GetRequiredService<ProxyMetrics>(), static snapshot => snapshot.Rejections.RateLimitedRequests == 2, timeout.Token).ConfigureAwait(false);
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            AssertEx.Equal(2, responses.Count(static response => response.Contains("200 OK", StringComparison.Ordinal)));
            AssertEx.Equal(2, responses.Count(static response => response.Contains("429 Too Many Requests", StringComparison.Ordinal)));
            AssertEx.Equal(2L, metrics.Rejections.RateLimitedRequests);
            AssertEx.Equal(0L, metrics.ClientConnections.Active);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task TimesOutIncompleteRequestHeadAsync()
    {
        var result = await RunProxyScenarioAsync("GET /slow", "", expectUpstreamConnection: false, timeoutMs: 150).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("408 Request Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal(1L, result.Metrics.ClientFailures.RequestHeadTimeouts);
    }

    public static async Task TimesOutIncompleteContentLengthRequestBodyAsync()
    {
        var result = await RunProxyScenarioAsync("POST /slow-body HTTP/1.1\r\nHost: body.test\r\nContent-Length: 10\r\n\r\nabc", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", readBodyFromUpstreamRequest: false, timeoutMs: 150).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("408 Request Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal(1L, result.Metrics.ClientFailures.RequestBodyTimeouts);
    }

    public static async Task TimesOutIncompleteChunkedRequestBodyAsync()
    {
        var result = await RunProxyScenarioAsync("POST /slow-chunk HTTP/1.1\r\nHost: chunk.test\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nabc", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", readBodyFromUpstreamRequest: false, timeoutMs: 150).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("408 Request Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal(1L, result.Metrics.ClientFailures.RequestBodyTimeouts);
    }

    public static async Task TimesOutMissingTerminatingChunkAfterCompleteChunkAsync()
    {
        var result = await RunProxyScenarioAsync("POST /slow-chunk HTTP/1.1\r\nHost: chunk.test\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n", "HTTP/1.1 500 Should Not Happen\r\nContent-Length: 0\r\n\r\n", readBodyFromUpstreamRequest: false, timeoutMs: 150).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("408 Request Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal(1L, result.Metrics.ClientFailures.RequestBodyTimeouts);
    }

    public static async Task UnavailableUpstreamProducesBadGatewayAsync()
    {
        var result = await RunProxyScenarioAsync("GET /unavailable HTTP/1.1\r\nHost: upstream.test\r\n\r\n", "", expectUpstreamConnection: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("502 Bad Gateway", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal(1L, result.Metrics.GeneratedResponses.BadGatewayResponses);
    }

    public static async Task UpstreamResponseHeadTimeoutProducesGatewayTimeoutAsync()
    {
        var result = await RunProxyScenarioAsync("GET /slow-upstream HTTP/1.1\r\nHost: upstream.test\r\n\r\n", "", readBodyFromUpstreamRequest: false, timeoutMs: 150, sendUpstreamResponse: false).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("504 Gateway Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal(1L, result.Metrics.UpstreamFailureReasons.ResponseHeadTimeouts);
        AssertEx.Equal(1L, result.Metrics.GeneratedResponses.GatewayTimeoutResponses);
    }

    public static async Task UpstreamContentLengthEarlyCloseClosesAfterStartedResponseAsync()
    {
        var result = await RunProxyScenarioAsync("GET /short HTTP/1.1\r\nHost: upstream.test\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nhello", readBodyFromUpstreamRequest: false, timeoutMs: 150).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.False(result.ClientResponse.Contains("502 Bad Gateway", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.False(result.ClientResponse.Contains("504 Gateway Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal(1L, result.Metrics.UpstreamForwarding.BodyRelayFailures);
    }

    public static async Task UpstreamChunkedEarlyCloseClosesAfterStartedResponseAsync()
    {
        var result = await RunProxyScenarioAsync("GET /short-chunk HTTP/1.1\r\nHost: upstream.test\r\n\r\n", "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhe", readBodyFromUpstreamRequest: false, timeoutMs: 150).ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.False(result.ClientResponse.Contains("502 Bad Gateway", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.False(result.ClientResponse.Contains("504 Gateway Timeout", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.Equal(1L, result.Metrics.UpstreamForwarding.BodyRelayFailures);
    }

    public static async Task HttpsListenerProxiesGetToUpstreamAsync()
    {
        var result = await RunTlsProxyScenarioAsync("home.test").ConfigureAwait(false);
        AssertEx.True(result.ClientResponse.Contains("200 OK", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.ClientResponse.EndsWith("proxied", StringComparison.Ordinal), result.ClientResponse);
        AssertEx.True(result.UpstreamRequest.StartsWith("GET /secure HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequest);
        AssertEx.Equal(1L, result.Metrics.Tls.HandshakeAttempts);
        AssertEx.Equal(1L, result.Metrics.Tls.HandshakeSuccesses);
    }

    public static async Task HttpsListenerSelectsCertificateBySniAsync()
    {
        var result = await RunTlsProxyScenarioAsync("alt.test", configureAltSni: true).ConfigureAwait(false);
        AssertEx.True(result.RemoteCertificateSubject.Contains("CN=alt.test", StringComparison.Ordinal), result.RemoteCertificateSubject);
        AssertEx.Equal(1L, result.Metrics.Tls.HandshakeSuccesses);
    }

    public static async Task HttpsListenerSelectsCertificateByCaseInsensitiveSniAsync()
    {
        var result = await RunTlsProxyScenarioAsync("ALT.TEST", configureAltSni: true).ConfigureAwait(false);
        AssertEx.True(result.RemoteCertificateSubject.Contains("CN=alt.test", StringComparison.Ordinal), result.RemoteCertificateSubject);
        AssertEx.Equal(1L, result.Metrics.Tls.HandshakeSuccesses);
    }

    public static async Task HttpsListenerUsesDefaultCertificateForUnmatchedSniAsync()
    {
        var result = await RunTlsProxyScenarioAsync("unmatched.test", configureAltSni: true).ConfigureAwait(false);
        AssertEx.True(result.RemoteCertificateSubject.Contains("CN=home.test", StringComparison.Ordinal), result.RemoteCertificateSubject);
        AssertEx.Equal(1L, result.Metrics.Tls.HandshakeSuccesses);
    }

    public static async Task HttpsListenerUsesDefaultCertificateWithoutSniAsync()
    {
        var result = await RunTlsProxyScenarioAsync("", configureAltSni: true).ConfigureAwait(false);
        AssertEx.True(result.RemoteCertificateSubject.Contains("CN=home.test", StringComparison.Ordinal), result.RemoteCertificateSubject);
        AssertEx.Equal(1L, result.Metrics.Tls.HandshakeSuccesses);
    }

    public static async Task HttpsListenerFailsHandshakeWhenNoCertificateMatchesAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-tls-{Guid.NewGuid():N}");
        try
        {
            TestCertificates.WriteSelfSignedPfx(Path.Combine(dataDirectory, "certs", "home.pfx"), "home.test");
            ConfigurationTests.WriteHttpsSite(dataDirectory, "tls.json", proxyPort, upstreamPort, "home-cert", includeDefault: false);
            ConfigurationTests.WriteOperationalConfig(dataDirectory, certificateId: "home-cert", certificatePath: "certs/home.pfx");
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
            var tlsStream = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
            await using var tlsStreamDisposal = tlsStream.ConfigureAwait(false);
            try
            {
                await tlsStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "unmatched.test" }).ConfigureAwait(false);
                throw new InvalidOperationException("Expected TLS authentication to fail.");
            }
            catch (Exception exception)when (exception is AuthenticationException or IOException)
            {
            }

            var metrics = await WaitForMetricsAsync(host.Services.GetRequiredService<ProxyMetrics>(), snapshot => snapshot.Tls.NoCertificateForSniFailures == 1, timeout.Token).ConfigureAwait(false);
            AssertEx.Equal(1L, metrics.Tls.HandshakeAttempts);
            AssertEx.Equal(1L, metrics.Tls.NoCertificateForSniFailures);
            AssertEx.Equal(1L, metrics.Tls.HandshakeFailures);
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task HttpsListenerTimesOutIncompleteTlsHandshakeAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-tls-{Guid.NewGuid():N}");
        try
        {
            TestCertificates.WriteSelfSignedPfx(Path.Combine(dataDirectory, "certs", "home.pfx"), "home.test");
            ConfigurationTests.WriteHttpsSite(dataDirectory, "tls.json", proxyPort, upstreamPort, "home-cert");
            ConfigurationTests.WriteOperationalConfig(dataDirectory, tlsHandshakeTimeoutMs: 150, certificateId: "home-cert", certificatePath: "certs/home.pfx");
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
            var metrics = await WaitForMetricsAsync(host.Services.GetRequiredService<ProxyMetrics>(), snapshot => snapshot.Tls.HandshakeTimeouts == 1, timeout.Token).ConfigureAwait(false);
            AssertEx.Equal(1L, metrics.Tls.HandshakeAttempts);
            AssertEx.Equal(1L, metrics.Tls.HandshakeTimeouts);
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task PersistentClientProcessesTwoSequentialGetsAndReusesUpstreamAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /one HTTP/1.1\r\nHost: keep.test\r\n\r\n", "GET /two HTTP/1.1\r\nHost: keep.test\r\nConnection: close\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none", "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\ntwo"]).ConfigureAwait(false);
        AssertEx.True(result.ClientResponses[0].EndsWith("one", StringComparison.Ordinal), result.ClientResponses[0]);
        AssertEx.True(result.ClientResponses[1].EndsWith("two", StringComparison.Ordinal), result.ClientResponses[1]);
        AssertEx.Equal(1, result.UpstreamAcceptedConnections);
        AssertEx.Equal(1L, result.Metrics.UpstreamPool.ConnectionsOpened);
        AssertEx.Equal(1L, result.Metrics.UpstreamPool.ConnectionsReused);
    }

    public static async Task ClientConnectionCloseHeaderClosesAfterResponseAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /close HTTP/1.1\r\nHost: close.test\r\nConnection: close\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"], expectClientCloseAfterLastResponse: true).ConfigureAwait(false);
        AssertEx.True(result.ClientClosedAfterLastResponse);
    }

    public static async Task Http10ClientClosesByDefaultAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /old HTTP/1.0\r\nHost: old.test\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"], expectClientCloseAfterLastResponse: true).ConfigureAwait(false);
        AssertEx.True(result.ClientClosedAfterLastResponse);
    }

    public static async Task MaxRequestsPerClientConnectionIsEnforcedAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /max HTTP/1.1\r\nHost: max.test\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"], maxRequestsPerClientConnection: 1, expectClientCloseAfterLastResponse: true).ConfigureAwait(false);
        AssertEx.True(result.ClientClosedAfterLastResponse);
        AssertEx.Equal(1L, result.Metrics.ClientConnections.ClosedByMaxRequests);
    }

    public static async Task ClientKeepAliveIdleTimeoutClosesConnectionAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /idle HTTP/1.1\r\nHost: idle.test\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"], clientKeepAliveIdleTimeoutMs: 150, expectClientCloseAfterLastResponse: true).ConfigureAwait(false);
        AssertEx.True(result.ClientClosedAfterLastResponse);
        AssertEx.Equal(1L, result.Metrics.ClientConnections.ClosedByIdleTimeout);
    }

    public static async Task MalformedSecondRequestClosesConnectionAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /first HTTP/1.1\r\nHost: malformed.test\r\n\r\n", "BAD\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nfirst"], readSecondAsRawClose: true).ConfigureAwait(false);
        AssertEx.True(result.ClientResponses[0].EndsWith("first", StringComparison.Ordinal), result.ClientResponses[0]);
        AssertEx.True(result.ClientResponses[1].Contains("400 Bad Request", StringComparison.Ordinal), result.ClientResponses[1]);
    }

    public static async Task PipelinedValidThenMalformedRequestDoesNotReachUpstreamTwiceAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /first HTTP/1.1\r\nHost: malformed.test\r\n\r\n", "BAD\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nfirst"], readSecondAsRawClose: true).ConfigureAwait(false);
        AssertEx.Equal(2, result.ClientResponses.Count);
        AssertEx.True(result.ClientResponses[0].Contains("200 OK", StringComparison.Ordinal), result.ClientResponses[0]);
        AssertEx.True(result.ClientResponses[1].Contains("400 Bad Request", StringComparison.Ordinal), result.ClientResponses[1]);
        AssertEx.Equal(1, result.UpstreamRequests.Count);
        AssertEx.True(result.UpstreamRequests[0].StartsWith("GET /first HTTP/1.1", StringComparison.Ordinal), result.UpstreamRequests[0]);
    }

    public static async Task PersistentClientProxiesContentLengthPostAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["POST /post HTTP/1.1\r\nHost: post.test\r\nContent-Length: 5\r\n\r\nhello", "GET /done HTTP/1.1\r\nHost: post.test\r\nConnection: close\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\npost", "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\ndone"]).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequests[0].EndsWith("hello", StringComparison.Ordinal), result.UpstreamRequests[0]);
        AssertEx.Equal(1, result.UpstreamAcceptedConnections);
    }

    public static async Task PersistentClientProxiesChunkedPostAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["POST /chunk HTTP/1.1\r\nHost: chunk.test\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n", "GET /done HTTP/1.1\r\nHost: chunk.test\r\nConnection: close\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nchunk", "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\ndone"]).ConfigureAwait(false);
        AssertEx.True(result.UpstreamRequests[0].Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase), result.UpstreamRequests[0]);
        AssertEx.Equal(1, result.UpstreamAcceptedConnections);
    }

    public static async Task UpstreamConnectionIsNotReusedAfterResponseConnectionCloseAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /first HTTP/1.1\r\nHost: upstream-close.test\r\n\r\n", "GET /second HTTP/1.1\r\nHost: upstream-close.test\r\nConnection: close\r\n\r\n"], ["HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 5\r\n\r\nfirst", "HTTP/1.1 200 OK\r\nContent-Length: 6\r\n\r\nsecond"]).ConfigureAwait(false);
        AssertEx.Equal(2, result.UpstreamAcceptedConnections);
        AssertEx.Equal(2L, result.Metrics.UpstreamPool.ConnectionsOpened);
    }

    public static async Task UpstreamConnectionIsNotReusedAfterPrematureDisconnectAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /short HTTP/1.1\r\nHost: short.test\r\nConnection: close\r\n\r\n", "GET /next HTTP/1.1\r\nHost: short.test\r\nConnection: close\r\n\r\n"], ["HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nshort", "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nnext"], closeUpstreamAfterEachResponse: true, useSeparateClients: true).ConfigureAwait(false);
        AssertEx.Equal(2, result.UpstreamAcceptedConnections);
        AssertEx.True(result.Metrics.UpstreamPool.ConnectionsDiscarded >= 1);
    }

    public static async Task UpstreamConnectionIsNotReusedAfterFramingErrorAsync()
    {
        var result = await RunPersistentClientScenarioAsync(["GET /bad-upstream HTTP/1.1\r\nHost: bad-upstream.test\r\nConnection: close\r\n\r\n", "GET /next HTTP/1.1\r\nHost: bad-upstream.test\r\nConnection: close\r\n\r\n"], ["NOT HTTP\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nnext"], closeUpstreamAfterEachResponse: true, useSeparateClients: true).ConfigureAwait(false);
        AssertEx.Equal(2, result.UpstreamAcceptedConnections);
        AssertEx.True(result.Metrics.UpstreamPool.ConnectionsDiscarded >= 1);
    }

    public static async Task WebSocketUpgradeOverPlaintextReturnsSwitchingProtocolsAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, "");
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            return await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        AssertEx.True(result.ClientObservation.Contains("101 Switching Protocols", StringComparison.Ordinal), result.ClientObservation);
        AssertEx.True(result.UpstreamResult.Request.Contains("Connection: Upgrade", StringComparison.OrdinalIgnoreCase), result.UpstreamResult.Request);
        AssertEx.Equal(1L, result.Metrics.Upgrades.RequestsSucceeded);
        AssertEx.Equal(1L, result.Metrics.Tunnels.Total);
    }

    public static async Task WebSocketUpgradeProducesTunnelDiagnosticAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, "");
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            return await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        var diagnostic = result.Diagnostics[0];
        AssertEx.Equal(101, diagnostic.ResponseStatusCode);
        AssertEx.True(diagnostic.IsUpgrade);
        AssertEx.True(diagnostic.TunnelEstablished);
        AssertEx.Equal("Closed", diagnostic.TunnelCloseReason);
    }

    public static async Task WebSocketTunnelRelaysClientBytesToUpstreamAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            var tunneled = await ReadExactTextAsync(stream, 5, cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, tunneled);
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            var response = await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
            await WriteTextAsync(stream, "hello", cancellationToken).ConfigureAwait(false);
            return response;
        }).ConfigureAwait(false);
        AssertEx.Equal("hello", result.UpstreamResult.Observation);
        AssertEx.Equal(5L, result.Metrics.Tunnels.BytesClientToUpstream);
    }

    public static async Task WebSocketTunnelRelaysUpstreamBytesToClientAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            await WriteTextAsync(stream, "world", cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, "");
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            var response = await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
            var tunneled = await ReadExactTextAsync(stream, 5, cancellationToken).ConfigureAwait(false);
            return response + tunneled;
        }).ConfigureAwait(false);
        AssertEx.True(result.ClientObservation.EndsWith("world", StringComparison.Ordinal), result.ClientObservation);
        AssertEx.Equal(5L, result.Metrics.Tunnels.BytesUpstreamToClient);
    }

    public static async Task WebSocketTunnelClosesWhenClientClosesAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            var buffer = new byte[1];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, bytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            return await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        AssertEx.Equal("0", result.UpstreamResult.Observation);
        AssertEx.Equal(1L, result.Metrics.Tunnels.Total);
    }

    public static async Task WebSocketTunnelClosesWhenUpstreamClosesAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, "");
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            var response = await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
            await WaitForClientCloseAsync(stream, cancellationToken).ConfigureAwait(false);
            return response;
        }).ConfigureAwait(false);
        AssertEx.True(result.ClientObservation.Contains("101 Switching Protocols", StringComparison.Ordinal), result.ClientObservation);
        AssertEx.Equal(1L, result.Metrics.Tunnels.Total);
    }

    public static async Task WebSocketTunnelIdleTimeoutClosesTunnelAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, "");
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            var response = await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
            await WaitForClientCloseAsync(stream, cancellationToken).ConfigureAwait(false);
            return response;
        }, tunnelIdleTimeoutMs: 150).ConfigureAwait(false);
        AssertEx.True(result.ClientObservation.Contains("101 Switching Protocols", StringComparison.Ordinal), result.ClientObservation);
        AssertEx.Equal(1L, result.Metrics.Tunnels.IdleTimeouts);
    }

    public static async Task WebSocketUpgradeOverHttpsReturnsSwitchingProtocolsAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, "");
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(host: "home.test"), cancellationToken).ConfigureAwait(false);
            return await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
        }, https: true).ConfigureAwait(false);
        AssertEx.True(result.ClientObservation.Contains("101 Switching Protocols", StringComparison.Ordinal), result.ClientObservation);
        AssertEx.Equal(1L, result.Metrics.Tls.HandshakeSuccesses);
        AssertEx.Equal(1L, result.Metrics.Upgrades.RequestsSucceeded);
    }

    public static async Task UpgradeDoesNotUseNormalUpstreamPoolAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, "");
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            return await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        AssertEx.Equal(0L, result.Metrics.UpstreamPool.ConnectionsOpened);
        AssertEx.Equal(0L, result.Metrics.UpstreamPool.ConnectionsReused);
        AssertEx.Equal(0L, result.Metrics.UpstreamPool.IdleConnections);
    }

    public static async Task MissingWebSocketHeadersAreRejectedAsync()
    {
        var result = await RunUpgradeRejectionScenarioAsync("GET /ws HTTP/1.1\r\nHost: ws.test\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\n\r\n").ConfigureAwait(false);
        AssertEx.True(result.ClientObservation.Contains("400 Bad Request", StringComparison.Ordinal), result.ClientObservation);
        AssertEx.Equal(1L, result.Metrics.Upgrades.RequestsRejected);
    }

    public static async Task UpstreamNon101UpgradeResponseIsForwardedAndClosedAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, "HTTP/1.1 403 Forbidden\r\nContent-Length: 7\r\n\r\ndenied!", cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, "");
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            return await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        AssertEx.True(result.ClientObservation.Contains("403 Forbidden", StringComparison.Ordinal), result.ClientObservation);
        AssertEx.True(result.ClientObservation.EndsWith("denied!", StringComparison.Ordinal), result.ClientObservation);
    }

    public static async Task MalformedSwitchingProtocolsResponseProducesBadGatewayAsync()
    {
        var result = await RunUpgradeScenarioAsync(async (stream, request, cancellationToken) =>
        {
            await WriteTextAsync(stream, "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n", cancellationToken).ConfigureAwait(false);
            return new UpgradeUpstreamResult(request, "");
        }, async (stream, cancellationToken) =>
        {
            await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
            return await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        AssertEx.True(result.ClientObservation.Contains("502 Bad Gateway", StringComparison.Ordinal), result.ClientObservation);
        AssertEx.Equal(1L, result.Metrics.Upgrades.UpstreamFailures);
    }

    public static async Task RoundRobinDistributesSequentialRequestsAcrossTwoUpstreamsAsync()
    {
        var result = await RunTwoUpstreamHttpScenarioAsync(healthCheckEnabled: false, requestsToSend: 4).ConfigureAwait(false);
        AssertEx.Equal(2, result.FirstRequests);
        AssertEx.Equal(2, result.SecondRequests);
        AssertEx.Equal(4L, result.Metrics.UpstreamSelections.Total);
    }

    public static async Task UnhealthyUpstreamIsNotSelectedAsync()
    {
        var result = await RunTwoUpstreamHttpScenarioAsync(healthCheckEnabled: true, requestsToSend: 2, startFirstUpstream: false, waitForFirstUnhealthy: true).ConfigureAwait(false);
        AssertEx.Equal(0, result.FirstRequests);
        AssertEx.Equal(2, result.SecondRequests);
        AssertEx.True(result.Upstreams.Any(static upstream => string.Equals(upstream.UpstreamName, "first", StringComparison.Ordinal) && upstream.HealthState == UpstreamHealthState.Unhealthy));
    }

    public static async Task AllUnhealthyUpstreamsReturnServiceUnavailableAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var firstPort = GetFreeTcpPort();
        var secondPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-all-unhealthy-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteSiteWithTwoUpstreams(dataDirectory, "pool.json", proxyPort, firstPort, secondPort, healthCheckEnabled: true);
            ConfigurationTests.WriteOperationalConfig(dataDirectory);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            await WaitForUpstreamStatusAsync(host, statuses => statuses.Count(status => status.HealthState == UpstreamHealthState.Unhealthy) == 2, timeout.Token).ConfigureAwait(false);
            var response = await SendSingleRequestAsync(proxyPort, "GET /anything HTTP/1.1\r\nHost: unhealthy.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            var diagnostic = host.Services.GetRequiredService<RecentRequestDiagnosticsStore>().Recent(10)[0];
            AssertEx.True(response.Contains("503 Service Unavailable", StringComparison.Ordinal), response);
            AssertEx.Equal(1L, metrics.Health.NoHealthyUpstreamFailures);
            AssertEx.Equal("NoHealthyUpstream", diagnostic.FailureKind);
            AssertEx.Equal(503, diagnostic.ResponseStatusCode);
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task WebSocketUpgradeUsesRoundRobinUpstreamSelectionAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var firstPort = GetFreeTcpPort();
        var secondPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-upgrade-lb-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteSiteWithTwoUpstreams(dataDirectory, "pool.json", proxyPort, firstPort, secondPort);
            ConfigurationTests.WriteOperationalConfig(dataDirectory);
            var firstTask = RunUpgradeCountingUpstreamAsync(firstPort, timeout.Token);
            var secondTask = RunUpgradeCountingUpstreamAsync(secondPort, timeout.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            var firstResponse = await SendUpgradeHandshakeAsync(proxyPort, timeout.Token).ConfigureAwait(false);
            var secondResponse = await SendUpgradeHandshakeAsync(proxyPort, timeout.Token).ConfigureAwait(false);
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            var firstCount = await firstTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var secondCount = await secondTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            AssertEx.True(firstResponse.Contains("101 Switching Protocols", StringComparison.Ordinal), firstResponse);
            AssertEx.True(secondResponse.Contains("101 Switching Protocols", StringComparison.Ordinal), secondResponse);
            AssertEx.Equal(1, firstCount);
            AssertEx.Equal(1, secondCount);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    public static async Task UpstreamPoolUsesDistinctEndpointKeysAsync()
    {
        var result = await RunTwoUpstreamHttpScenarioAsync(healthCheckEnabled: false, requestsToSend: 2).ConfigureAwait(false);
        AssertEx.Equal(1, result.FirstRequests);
        AssertEx.Equal(1, result.SecondRequests);
        AssertEx.Equal(2L, result.Metrics.UpstreamPool.ConnectionsOpened);
        AssertEx.Equal(0L, result.Metrics.UpstreamPool.ConnectionsReused);
    }

    private static async Task<string> RunSingleResponseUpstreamAsync(int upstreamPort, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await using var streamDisposal = stream.ConfigureAwait(false);
            var requestText = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
            var responseBytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\nContent-Type: text/plain\r\n\r\nproxied");
            await stream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
            return requestText;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<ProxyScenarioResult> RunProxyScenarioAsync(string clientRequest, string upstreamResponse, bool readBodyFromUpstreamRequest = true, bool expectUpstreamConnection = true, int? timeoutMs = null, bool sendUpstreamResponse = true, bool? accessLogEnabled = null, int? recentDiagnosticsCapacity = null, int? maxRequestHeadBytes = null, int? maxHeaderCount = null, int? maxHeaderLineBytes = null, long? maxRequestBodyBytes = null, int? requestsPerMinutePerIp = null, int? upgradeRequestsPerMinutePerIp = null)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-integration-{Guid.NewGuid():N}");
        ConfigurationTests.WriteSite(dataDirectory, "scenario.json", proxyPort, upstreamPort);
        if (timeoutMs.HasValue || accessLogEnabled.HasValue || recentDiagnosticsCapacity.HasValue || maxRequestHeadBytes.HasValue || maxHeaderCount.HasValue || maxHeaderLineBytes.HasValue || maxRequestBodyBytes.HasValue || requestsPerMinutePerIp.HasValue || upgradeRequestsPerMinutePerIp.HasValue)
        {
            ConfigurationTests.WriteOperationalConfig(dataDirectory, clientRequestHeadTimeoutMs: timeoutMs ?? 1000, clientRequestBodyIdleTimeoutMs: timeoutMs ?? 1000, upstreamConnectTimeoutMs: timeoutMs ?? 1000, upstreamResponseHeadTimeoutMs: timeoutMs ?? 1000, upstreamResponseBodyIdleTimeoutMs: timeoutMs ?? 1000, downstreamWriteTimeoutMs: 1000, accessLogEnabled: accessLogEnabled ?? true, recentDiagnosticsCapacity: recentDiagnosticsCapacity ?? 500, maxRequestHeadBytes: maxRequestHeadBytes ?? 32768, maxHeaderCount: maxHeaderCount ?? 128, maxHeaderLineBytes: maxHeaderLineBytes ?? 8192, maxRequestBodyBytes: maxRequestBodyBytes ?? 104857600, requestsPerMinutePerIp: requestsPerMinutePerIp ?? 240, upgradeRequestsPerMinutePerIp: upgradeRequestsPerMinutePerIp ?? 30);
        }

        var upstreamTask = expectUpstreamConnection ? RunScenarioUpstreamAsync(upstreamPort, upstreamResponse, readBodyFromUpstreamRequest, sendUpstreamResponse, timeout.Token) : Task.FromResult("");
        try
        {
            using var host = Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
            {
                builder.Sources.Clear();
                builder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Mdrava:DataDirectory"] = dataDirectory });
            }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) =>
            {
                services.AddProxyDataPlane(context.Configuration);
            }).Build();
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            await WaitForProxyRuntimeAsync(host, timeout.Token).ConfigureAwait(false);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                var requestBytes = Encoding.ASCII.GetBytes(WithConnectionClose(clientRequest));
                await stream.WriteAsync(requestBytes, timeout.Token).ConfigureAwait(false);
                var clientResponse = await ReadToEndAsync(stream, timeout.Token).ConfigureAwait(false);
                var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
                var diagnostics = host.Services.GetRequiredService<RecentRequestDiagnosticsStore>().Recent(50);
                return new ProxyScenarioResult(clientResponse, upstreamRequest, metrics, diagnostics);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task<ProxyScenarioResult> RunCustomProxyScenarioAsync(Func<int, int, string> siteJsonFactory, string clientRequest, string upstreamResponse, bool readBodyFromUpstreamRequest = true, bool expectUpstreamConnection = true, bool sendUpstreamResponse = true, Action<string>? configureOperational = null)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-integration-{Guid.NewGuid():N}");
        ConfigurationTests.WriteCustomSite(dataDirectory, "scenario.json", siteJsonFactory(proxyPort, upstreamPort));
        configureOperational?.Invoke(dataDirectory);
        var upstreamTask = expectUpstreamConnection ? RunScenarioUpstreamAsync(upstreamPort, upstreamResponse, readBodyFromUpstreamRequest, sendUpstreamResponse, timeout.Token) : Task.FromResult("");
        try
        {
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            await WaitForProxyRuntimeAsync(host, timeout.Token).ConfigureAwait(false);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(WithConnectionClose(clientRequest)), timeout.Token).ConfigureAwait(false);
                var clientResponse = await ReadToEndAsync(stream, timeout.Token).ConfigureAwait(false);
                var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
                var diagnostics = host.Services.GetRequiredService<RecentRequestDiagnosticsStore>().Recent(50);
                return new ProxyScenarioResult(clientResponse, upstreamRequest, metrics, diagnostics);
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

    private static string SiteWithSingleProxyRoute(int proxyPort, int upstreamPort)
    {
        return SiteWithSingleProxyRoute(proxyPort, upstreamPort, "");
    }

    private static async Task WaitForProxyRuntimeAsync(IHost host, CancellationToken cancellationToken)
    {
        var runtimeState = host.Services.GetRequiredService<ProxyRuntimeState>();
        while (true)
        {
            var snapshot = runtimeState.Snapshot();
            if (snapshot.IsRunning)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.LastError))
            {
                throw new InvalidOperationException($"Proxy runtime did not start: {snapshot.LastError}");
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string SiteWithSingleProxyRoute(int proxyPort, int upstreamPort, string routeExtraJson)
    {
        return SiteWithRoutes(proxyPort, $$"""
                {
                  "name": "scenario",
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
            """);
    }

    private static string SiteWithRoutes(int proxyPort, string routesJson)
    {
        return $$"""
        {
          "name": "scenario",
          "listeners": [
            {
              "name": "main",
              "address": "127.0.0.1",
              "port": {{proxyPort}}
            }
          ],
          "host": "*",
          "routes": [
            {{routesJson}}
          ]
        }
        """;
    }

    private static async Task<TwoUpstreamHttpResult> RunTwoUpstreamHttpScenarioAsync(bool healthCheckEnabled, int requestsToSend, bool startFirstUpstream = true, bool waitForFirstUnhealthy = false)
    {
        var proxyPort = GetFreeTcpPort();
        var firstPort = GetFreeTcpPort();
        var secondPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var upstreamStop = new CancellationTokenSource();
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-lb-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteSiteWithTwoUpstreams(dataDirectory, "pool.json", proxyPort, firstPort, secondPort, healthCheckEnabled: healthCheckEnabled);
            ConfigurationTests.WriteOperationalConfig(dataDirectory);
            var firstTask = startFirstUpstream ? RunReusableHttpUpstreamAsync(firstPort, "first", upstreamStop.Token) : Task.FromResult(0);
            var secondTask = RunReusableHttpUpstreamAsync(secondPort, "second", upstreamStop.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            if (waitForFirstUnhealthy)
            {
                await WaitForUpstreamStatusAsync(host, statuses => statuses.Any(static status => string.Equals(status.UpstreamName, "first", StringComparison.Ordinal) && status.HealthState == UpstreamHealthState.Unhealthy), timeout.Token).ConfigureAwait(false);
            }

            for (var index = 0; index < requestsToSend; index++)
            {
                var response = await SendSingleRequestAsync(proxyPort, $"GET /request-{index} HTTP/1.1\r\nHost: lb.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
                AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            }

            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await upstreamStop.CancelAsync().ConfigureAwait(false);
            var firstRequests = await firstTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var secondRequests = await secondTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            var diagnostics = host.Services.GetRequiredService<RecentRequestDiagnosticsStore>().Recent(50);
            var configuration = host.Services.GetRequiredService<Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.IProxyConfigurationStore>().Snapshot;
            var upstreams = host.Services.GetRequiredService<UpstreamHealthStore>().Snapshot(ProxyUpstreamHealthSourceMapper.FromRoutes(configuration.Routes)).ToArray();
            return new TwoUpstreamHttpResult(firstRequests, secondRequests, metrics, upstreams, diagnostics);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    private static async Task<UpgradeScenarioResult> RunUpgradeScenarioAsync(Func<NetworkStream, string, CancellationToken, Task<UpgradeUpstreamResult>> upstreamHandler, Func<Stream, CancellationToken, Task<string>> clientHandler, bool https = false, int tunnelIdleTimeoutMs = 1000, int maxActiveUpgradedTunnels = 1024)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-upgrade-{Guid.NewGuid():N}");
        try
        {
            if (https)
            {
                TestCertificates.WriteSelfSignedPfx(Path.Combine(dataDirectory, "certs", "home.pfx"), "home.test");
                ConfigurationTests.WriteHttpsSite(dataDirectory, "upgrade.json", proxyPort, upstreamPort, "home-cert");
                ConfigurationTests.WriteOperationalConfig(dataDirectory, tunnelIdleTimeoutMs: tunnelIdleTimeoutMs, maxActiveUpgradedTunnels: maxActiveUpgradedTunnels, certificateId: "home-cert", certificatePath: "certs/home.pfx");
            }
            else
            {
                ConfigurationTests.WriteSite(dataDirectory, "upgrade.json", proxyPort, upstreamPort);
                ConfigurationTests.WriteOperationalConfig(dataDirectory, tunnelIdleTimeoutMs: tunnelIdleTimeoutMs, maxActiveUpgradedTunnels: maxActiveUpgradedTunnels);
            }

            var upstreamTask = RunUpgradeUpstreamAsync(upstreamPort, upstreamHandler, timeout.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            string clientObservation;
            UpgradeUpstreamResult upstreamResult;
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
                if (https)
                {
                    var tlsStream = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
                    await using var tlsStreamDisposal = tlsStream.ConfigureAwait(false);
                    await tlsStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "home.test" }, timeout.Token).ConfigureAwait(false);
                    clientObservation = await clientHandler(tlsStream, timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    var stream = client.GetStream();
                    await using var streamDisposal = stream.ConfigureAwait(false);
                    clientObservation = await clientHandler(stream, timeout.Token).ConfigureAwait(false);
                }

                try
                {
                    upstreamResult = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception)
                {
                    throw new InvalidOperationException($"Upgrade upstream did not receive a request. Client observed: {clientObservation}", exception);
                }
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            var diagnostics = host.Services.GetRequiredService<RecentRequestDiagnosticsStore>().Recent(50);
            return new UpgradeScenarioResult(clientObservation, upstreamResult, metrics, diagnostics);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    private static async Task<UpgradeScenarioResult> RunUpgradeRejectionScenarioAsync(string request)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-upgrade-reject-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteSite(dataDirectory, "upgrade.json", proxyPort, upstreamPort);
            ConfigurationTests.WriteOperationalConfig(dataDirectory);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            string clientObservation;
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                await WriteTextAsync(stream, request, timeout.Token).ConfigureAwait(false);
                clientObservation = await ReadToEndAsync(stream, timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            var diagnostics = host.Services.GetRequiredService<RecentRequestDiagnosticsStore>().Recent(50);
            return new UpgradeScenarioResult(clientObservation, new UpgradeUpstreamResult("", ""), metrics, diagnostics);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
    }

    private static async Task<UpgradeUpstreamResult> RunUpgradeUpstreamAsync(int upstreamPort, Func<NetworkStream, string, CancellationToken, Task<UpgradeUpstreamResult>> handler, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await using var streamDisposal = stream.ConfigureAwait(false);
            var request = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
            return await handler(stream, request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<int> RunUpgradeCountingUpstreamAsync(int upstreamPort, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        var count = 0;
        try
        {
            while (count < 1)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                count++;
                await WriteTextAsync(stream, SwitchingProtocolsResponse(), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }

        return count;
    }

    private static async Task<int> RunReusableHttpUpstreamAsync(int upstreamPort, string name, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        var requestCount = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var request = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(request))
                    {
                        break;
                    }

                    if (request.Contains("GET /health ", StringComparison.Ordinal))
                    {
                        await WriteTextAsync(stream, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    requestCount++;
                    var body = name;
                    await WriteTextAsync(stream, $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n\r\n{body}", cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }

        return requestCount;
    }

    private static async Task<IReadOnlyList<ProxyUpstreamStatus>> WaitForUpstreamStatusAsync(IHost host, Func<IReadOnlyList<ProxyUpstreamStatus>, bool> predicate, CancellationToken cancellationToken)
    {
        var healthStore = host.Services.GetRequiredService<UpstreamHealthStore>();
        var configurationStore = host.Services.GetRequiredService<Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.IProxyConfigurationStore>();
        while (true)
        {
            var records = healthStore.Snapshot(ProxyUpstreamHealthSourceMapper.FromRoutes(configurationStore.Snapshot.Routes));
            if (predicate(records))
            {
                return records;
            }

            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> SendUpgradeHandshakeAsync(int proxyPort, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort, cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();
        await using var streamDisposal = stream.ConfigureAwait(false);
        await WriteTextAsync(stream, WebSocketRequest(), cancellationToken).ConfigureAwait(false);
        return await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TlsProxyScenarioResult> RunTlsProxyScenarioAsync(string targetHost, bool configureAltSni = false)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-tls-{Guid.NewGuid():N}");
        try
        {
            TestCertificates.WriteSelfSignedPfx(Path.Combine(dataDirectory, "certs", "home.pfx"), "home.test");
            if (configureAltSni)
            {
                TestCertificates.WriteSelfSignedPfx(Path.Combine(dataDirectory, "certs", "alt.pfx"), "alt.test");
                WriteDualCertificateOperationalConfig(dataDirectory);
                WriteDualCertificateHttpsSite(dataDirectory, proxyPort, upstreamPort);
            }
            else
            {
                ConfigurationTests.WriteHttpsSite(dataDirectory, "tls.json", proxyPort, upstreamPort, "home-cert");
                ConfigurationTests.WriteOperationalConfig(dataDirectory, certificateId: "home-cert", certificatePath: "certs/home.pfx");
            }

            var upstreamTask = RunSingleResponseUpstreamAsync(upstreamPort, timeout.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
                var tlsStream = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
                await using var tlsStreamDisposal = tlsStream.ConfigureAwait(false);
                await tlsStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = targetHost }, timeout.Token).ConfigureAwait(false);
                var remoteCertificate = new X509Certificate2(tlsStream.RemoteCertificate!);
                var requestBytes = Encoding.ASCII.GetBytes("GET /secure HTTP/1.1\r\nHost: home.test\r\nConnection: close\r\n\r\n");
                await tlsStream.WriteAsync(requestBytes, timeout.Token).ConfigureAwait(false);
                var clientResponse = await ReadToEndAsync(tlsStream, timeout.Token).ConfigureAwait(false);
                var upstreamRequest = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
                return new TlsProxyScenarioResult(clientResponse, upstreamRequest, remoteCertificate.Subject, metrics);
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

    private static async Task<PersistentClientScenarioResult> RunPersistentClientScenarioAsync(IReadOnlyList<string> clientRequests, IReadOnlyList<string> upstreamResponses, int maxRequestsPerClientConnection = 100, int clientKeepAliveIdleTimeoutMs = 1000, bool expectClientCloseAfterLastResponse = false, bool readSecondAsRawClose = false, bool closeUpstreamAfterEachResponse = false, bool useSeparateClients = false)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"mdrava-persistent-{Guid.NewGuid():N}");
        try
        {
            ConfigurationTests.WriteSite(dataDirectory, "persistent.json", proxyPort, upstreamPort);
            ConfigurationTests.WriteOperationalConfig(dataDirectory, clientKeepAliveIdleTimeoutMs: clientKeepAliveIdleTimeoutMs, maxRequestsPerClientConnection: maxRequestsPerClientConnection);
            var upstreamTask = RunPersistentScenarioUpstreamAsync(upstreamPort, upstreamResponses, closeUpstreamAfterEachResponse, timeout.Token);
            using var host = BuildProxyHost(dataDirectory);
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            List<string> clientResponses = [];
            try
            {
                if (useSeparateClients)
                {
                    foreach (var request in clientRequests)
                    {
                        clientResponses.Add(await SendSingleRequestAsync(proxyPort, request, timeout.Token).ConfigureAwait(false));
                    }
                }
                else
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, proxyPort, timeout.Token).ConfigureAwait(false);
                    var stream = client.GetStream();
                    await using var streamDisposal = stream.ConfigureAwait(false);
                    for (var index = 0; index < clientRequests.Count; index++)
                    {
                        var requestBytes = Encoding.ASCII.GetBytes(clientRequests[index]);
                        await stream.WriteAsync(requestBytes, timeout.Token).ConfigureAwait(false);
                        if (readSecondAsRawClose && index == 1)
                        {
                            clientResponses.Add(await ReadToEndAsync(stream, timeout.Token).ConfigureAwait(false));
                            break;
                        }

                        clientResponses.Add(await ReadHttpResponseAsync(stream, timeout.Token).ConfigureAwait(false));
                    }

                    if (expectClientCloseAfterLastResponse)
                    {
                        await WaitForClientCloseAsync(stream, timeout.Token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var upstreamResult = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            return new PersistentClientScenarioResult(clientResponses, upstreamResult.Requests, upstreamResult.AcceptedConnections, expectClientCloseAfterLastResponse, metrics);
        }
        finally
        {
            DeleteDirectory(dataDirectory);
        }
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

    private static async Task WaitForClientCloseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        while (true)
        {
            try
            {
                var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return;
                }
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    private static async Task<PersistentUpstreamResult> RunPersistentScenarioUpstreamAsync(int upstreamPort, IReadOnlyList<string> responses, bool closeAfterEachResponse, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        List<string> requests = [];
        var acceptedConnections = 0;
        try
        {
            while (requests.Count < responses.Count)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                acceptedConnections++;
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                while (requests.Count < responses.Count)
                {
                    var requestText = await ReadHttpRequestAsync(stream, readBody: true, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(requestText))
                    {
                        break;
                    }

                    requests.Add(requestText);
                    var response = responses[requests.Count - 1];
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    if (closeAfterEachResponse || response.Contains("\r\nConnection: close", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }

            return new PersistentUpstreamResult(requests, acceptedConnections);
        }
        finally
        {
            listener.Stop();
        }
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

    private static void WriteDualCertificateOperationalConfig(string dataDirectory)
    {
        var configDirectory = Directory.CreateDirectory(Path.Combine(dataDirectory, "config")).FullName;
        File.WriteAllText(Path.Combine(configDirectory, "proxy.json"), """
            {
              "certificates": [
                {
                  "id": "home-cert",
                  "format": "pfx",
                  "path": "certs/home.pfx"
                },
                {
                  "id": "alt-cert",
                  "format": "pfx",
                  "path": "certs/alt.pfx"
                }
              ]
            }
            """);
    }

    private static void WriteDualCertificateHttpsSite(string dataDirectory, int proxyPort, int upstreamPort)
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        File.WriteAllText(Path.Combine(sites, "tls.json"), $$"""
            {
              "name": "tls",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}},
                  "transport": "https",
                  "defaultCertificateId": "home-cert",
                  "sniCertificates": [
                    {
                      "hostName": "alt.test",
                      "certificateId": "alt-cert"
                    }
                  ]
                }
              ],
              "host": "*",
              "pathPrefix": "/",
              "upstreams": [
                {
                  "name": "local-test",
                  "address": "127.0.0.1",
                  "port": {{upstreamPort}}
                }
              ]
            }
            """);
    }

    private static async Task<ProxyMetricsSnapshot> WaitForMetricsAsync(ProxyMetrics metrics, Func<ProxyMetricsSnapshot, bool> predicate, CancellationToken cancellationToken)
    {
        while (true)
        {
            var snapshot = metrics.Snapshot();
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string WebSocketRequest(string host = "ws.test")
    {
        return "GET /ws HTTP/1.1\r\n" + $"Host: {host}\r\n" + "Connection: Upgrade\r\n" + "Upgrade: websocket\r\n" + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" + "Sec-WebSocket-Version: 13\r\n" + "Origin: https://origin.test\r\n" + "\r\n";
    }

    private static string SwitchingProtocolsResponse()
    {
        return "HTTP/1.1 101 Switching Protocols\r\n" + "Connection: Upgrade\r\n" + "Upgrade: websocket\r\n" + "Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\n" + "\r\n";
    }

    private static async Task WriteTextAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> RunScenarioUpstreamAsync(int upstreamPort, string upstreamResponse, bool readBody, bool sendResponse, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await using var streamDisposal = stream.ConfigureAwait(false);
            var requestText = await ReadHttpRequestAsync(stream, readBody, cancellationToken).ConfigureAwait(false);
            if (sendResponse)
            {
                var responseBytes = Encoding.ASCII.GetBytes(upstreamResponse);
                await stream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            return requestText;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string WithConnectionClose(string request)
    {
        var separator = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (separator < 0 || request.Contains("\r\nConnection:", StringComparison.OrdinalIgnoreCase))
        {
            return request;
        }

        return request.Insert(separator, "\r\nConnection: close");
    }

    private static async Task<string> ReadHttpRequestAsync(NetworkStream stream, bool readBody, CancellationToken cancellationToken)
    {
        var headAndMaybeBody = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!readBody)
        {
            return headAndMaybeBody;
        }

        var separator = headAndMaybeBody.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var head = separator >= 0 ? headAndMaybeBody[..(separator + 4)] : headAndMaybeBody;
        var body = separator >= 0 ? headAndMaybeBody[(separator + 4)..] : "";
        if (head.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
        {
            while (!body.Contains("0\r\n\r\n", StringComparison.Ordinal) && !body.Contains("0\r\nX-Trailer: ok\r\n\r\n", StringComparison.Ordinal))
            {
                body += await ReadSomeAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (TryGetContentLength(head, out var contentLength))
        {
            while (Encoding.ASCII.GetByteCount(body) < contentLength)
            {
                body += await ReadSomeAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }

        return head + body;
    }

    private static async Task<string> ReadSomeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (bytesRead == 0)
        {
            return "";
        }

        return Encoding.ASCII.GetString(buffer, 0, bytesRead);
    }

    private static bool TryGetContentLength(string head, out int contentLength)
    {
        contentLength = 0;
        foreach (var line in head.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture, out contentLength);
            }
        }

        return false;
    }

    private static async Task<string> ReadRequestHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var total = 0;
        while (total < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            total += bytesRead;
            if (ContainsRequestHeadTerminator(buffer.AsSpan(0, total)))
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static async Task<string> ReadHttpResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = await ReadResponseHeadAsync(stream, cancellationToken).ConfigureAwait(false);
        if (TryGetContentLength(head, out var contentLength))
        {
            return head + await ReadExactTextAsync(stream, contentLength, cancellationToken).ConfigureAwait(false);
        }

        if (head.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
        {
            var body = new StringBuilder();
            while (true)
            {
                var line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
                body.Append(line);
                if (string.Equals(line, "0\r\n", StringComparison.Ordinal))
                {
                    while (true)
                    {
                        var trailerLine = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
                        body.Append(trailerLine);
                        if (string.Equals(trailerLine, "\r\n", StringComparison.Ordinal))
                        {
                            return head + body;
                        }
                    }
                }

                var sizeText = line[..^2].Split(';')[0];
                var size = Convert.ToInt32(sizeText, 16);
                body.Append(await ReadExactTextAsync(stream, size + 2, cancellationToken).ConfigureAwait(false));
            }
        }

        return head;
    }

    private static async Task<string> ReadResponseHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var total = 0;
        while (total < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(total, 1), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            total += bytesRead;
            if (ContainsRequestHeadTerminator(buffer.AsSpan(0, total)))
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        List<byte> bytes = [];
        var previous = (byte)0;
        var buffer = new byte[1];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            var current = buffer[0];
            bytes.Add(current);
            if (previous == (byte)'\r' && current == (byte)'\n')
            {
                break;
            }

            previous = current;
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static async Task<string> ReadExactTextAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var total = 0;
        while (total < length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(total, length - total), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            total += bytesRead;
        }

        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static async Task<string> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var builder = new StringBuilder();
        while (true)
        {
            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                break;
            }

            if (bytesRead == 0)
            {
                break;
            }

            builder.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
        }

        return builder.ToString();
    }

    private static bool ContainsRequestHeadTerminator(ReadOnlySpan<byte> bytes)
    {
        for (var index = 3; index < bytes.Length; index++)
        {
            if (bytes[index - 3] == (byte)'\r' && bytes[index - 2] == (byte)'\n' && bytes[index - 1] == (byte)'\r' && bytes[index] == (byte)'\n')
            {
                return true;
            }
        }

        return false;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed record ProxyScenarioResult(string ClientResponse, string UpstreamRequest, ProxyMetricsSnapshot Metrics, IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Diagnostics);
    private sealed record TlsProxyScenarioResult(string ClientResponse, string UpstreamRequest, string RemoteCertificateSubject, ProxyMetricsSnapshot Metrics);
    private sealed record PersistentUpstreamResult(IReadOnlyList<string> Requests, int AcceptedConnections);
    private sealed record PersistentClientScenarioResult(IReadOnlyList<string> ClientResponses, IReadOnlyList<string> UpstreamRequests, int UpstreamAcceptedConnections, bool ClientClosedAfterLastResponse, ProxyMetricsSnapshot Metrics);
    private sealed record UpgradeUpstreamResult(string Request, string Observation);
    private sealed record UpgradeScenarioResult(string ClientObservation, UpgradeUpstreamResult UpstreamResult, ProxyMetricsSnapshot Metrics, IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Diagnostics);
    private sealed record TwoUpstreamHttpResult(int FirstRequests, int SecondRequests, ProxyMetricsSnapshot Metrics, IReadOnlyList<ProxyUpstreamStatus> Upstreams, IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Diagnostics);
}
