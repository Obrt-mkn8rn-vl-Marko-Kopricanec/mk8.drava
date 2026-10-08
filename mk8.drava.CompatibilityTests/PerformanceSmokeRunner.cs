using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class PerformanceSmokeRunner
{
    private const string Routing = "Routing";
    private const string Config = "Config";
    private const string Http1 = "HTTP1";
    private const string Cache = "Cache";
    private const string Headers = "Headers";
    private static readonly string[] Domains = [Routing, Config, Http1, Cache, Headers];
    public static bool IsPerformanceCommand(string[] args)
    {
        return args.Any(static arg => string.Equals(arg, "--performance", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--list-performance-domains", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> RunAsync(string[] args)
    {
        PerformanceSmokeOptions options;
        try
        {
            options = PerformanceSmokeOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            await Console.Error.WriteLineAsync("Use --list-performance-domains to see supported performance domains.").ConfigureAwait(false);
            return 2;
        }

        if (options.ListDomains)
        {
            foreach (var domain in Domains)
            {
                Console.WriteLine(domain);
            }

            return 0;
        }

        var selectedDomains = options.Domains.Count == 0 ? Domains : Domains.Where(options.Domains.Contains).ToArray();
        if (selectedDomains.Length == 0)
        {
            await Console.Error.WriteLineAsync($"No performance domains matched: {string.Join(", ", options.Domains)}").ConfigureAwait(false);
            return 2;
        }

        var failures = 0;
        List<PerformanceSmokeResult> results = [];
        List<string> failureDomains = [];
        foreach (var domain in selectedDomains)
        {
            try
            {
                var result = await RunDomainAsync(domain).ConfigureAwait(false);
                results.Add(result);
                var status = result.Passed ? "PASS" : "FAIL";
                Console.WriteLine($"{status} Performance {result.Domain}: {result.Operations} operations in {result.Elapsed.TotalMilliseconds:F1} ms; threshold {result.Threshold.TotalMilliseconds:F1} ms; {result.Detail}");
                if (!result.Passed)
                {
                    failures++;
                    failureDomains.Add(result.Domain);
                }
            }
            catch (Exception exception)
            {
                failures++;
                failureDomains.Add(domain);
                await Console.Error.WriteLineAsync($"FAIL Performance {domain}: correctness failure before threshold evaluation.").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            }
        }

        WritePerformanceSummary(options, selectedDomains, results, failures, failureDomains);
        if (failures > 0)
        {
            return 1;
        }

        Console.WriteLine($"Passed {selectedDomains.Length} performance smoke domains.");
        return 0;
    }

    private static void WritePerformanceSummary(PerformanceSmokeOptions options, string[] selectedDomains, IReadOnlyList<PerformanceSmokeResult> results, int failures, IReadOnlyList<string> failureDomains)
    {
        if (string.IsNullOrWhiteSpace(options.SummaryFile))
        {
            return;
        }

        var directory = Path.GetDirectoryName(options.SummaryFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var summary = new
        {
            kind = "performance",
            status = failures == 0 ? "passed" : "failed",
            selectedDomains,
            passedDomains = selectedDomains.Length - failures,
            failedDomains = failures,
            failures = failureDomains,
            results = results.Select(static result => new { domain = result.Domain, operations = result.Operations, elapsedMilliseconds = result.Elapsed.TotalMilliseconds, thresholdMilliseconds = result.Threshold.TotalMilliseconds, passed = result.Passed, detail = result.Detail }).ToArray()
        };
        File.WriteAllText(options.SummaryFile, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Task<PerformanceSmokeResult> RunDomainAsync(string domain)
    {
        return domain switch
        {
            Routing => RunRoutingAsync(),
            Config => RunConfigAsync(),
            Http1 => RunHttp1Async(),
            Cache => RunCacheAsync(),
            Headers => RunHeadersAsync(),
            _ => throw new ArgumentException($"Unknown performance domain: {domain}")};
    }

    private static Task<PerformanceSmokeResult> RunRoutingAsync()
    {
        const int routeCount = 64;
        const int warmup = 1_000;
        const int operations = 50_000;
        var matcher = new SingleUpstreamRouteMatcher();
        var snapshot = CreateSnapshot(routeCount, cacheEnabled: false);
        var routeCandidates = snapshot.Routes.Select(static route => new RouteMatchCandidate(route.Host, route.PathPrefix)).ToArray();
        var request = new Http1RequestHead("GET", "/svc42/resource?id=1", "/svc42/resource", "HTTP/1.1", "perf.test", Http1RequestFraming.None, []);
        var matchRequest = new RouteMatchRequest(request.Host, request.Path);
        for (var index = 0; index < warmup; index++)
        {
            matcher.Match(routeCandidates, matchRequest);
        }

        var stopwatch = Stopwatch.StartNew();
        RouteMatch? match = null;
        for (var index = 0; index < operations; index++)
        {
            match = matcher.Match(routeCandidates, matchRequest);
        }

        stopwatch.Stop();
        AssertEx.NotNull(match);
        AssertEx.Equal("route-42", snapshot.Routes[match!.RouteIndex].Name);
        return Task.FromResult(new PerformanceSmokeResult(Routing, operations, stopwatch.Elapsed, TimeSpan.FromSeconds(5), $"{routeCount} configured routes"));
    }

    private static async Task<PerformanceSmokeResult> RunConfigAsync()
    {
        const int sites = 8;
        const int operations = 15;
        using var temp = TemporaryDirectory.Create("mdrava-perf-config");
        for (var index = 0; index < sites; index++)
        {
            ConfigurationTests.WriteCustomSite(temp.Path, $"site-{index}.json", SiteJson($"site-{index}", 18_000 + index, 19_000 + index));
        }

        var loader = CreateLoader(temp.Path);
        var warmup = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertLoaded(warmup);
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < operations; index++)
        {
            var result = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
            var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot));
            AssertEx.Equal(sites, projection.Routes.Count);
        }

        stopwatch.Stop();
        return new PerformanceSmokeResult(Config, operations, stopwatch.Elapsed, TimeSpan.FromSeconds(10), $"{sites} JSON site files loaded and projected each operation");
    }

    private static async Task<PerformanceSmokeResult> RunHttp1Async()
    {
        const int staticRequests = 30;
        const int proxyRequests = 30;
        var staticElapsed = await RunHttp1StaticResponseAsync(staticRequests).ConfigureAwait(false);
        var proxyElapsed = await RunHttp1ProxyAsync(proxyRequests).ConfigureAwait(false);
        var elapsed = staticElapsed + proxyElapsed;
        return new PerformanceSmokeResult(Http1, staticRequests + proxyRequests, elapsed, TimeSpan.FromSeconds(20), $"static={staticElapsed.TotalMilliseconds:F1} ms; proxy={proxyElapsed.TotalMilliseconds:F1} ms");
    }

    private static Task<PerformanceSmokeResult> RunCacheAsync()
    {
        const int warmup = 1_000;
        const int operations = 75_000;
        var cache = new ResponseCacheStore(TimeProvider.System);
        var snapshot = CreateSnapshot(routeCount: 1, cacheEnabled: true);
        var route = snapshot.Routes[0];
        var listener = snapshot.Listeners[0];
        var cacheScope = CacheScope(route, listener);
        var request = new Http1RequestHead("GET", "/svc0/resource?id=1", "/svc0/resource", "HTTP/1.1", "perf.test", Http1RequestFraming.None, [new ProxyHeaderField("Accept", "text/plain")]);
        var responseHeaders = new[]
        {
            new ProxyHeaderField("Content-Type", "text/plain"),
            new ProxyHeaderField("Cache-Control", "max-age=60")
        };
        var response = new Http1ResponseHead("HTTP/1.1", 200, "OK", Http1ResponseFraming.FromContentLength(6), responseHeaders);
        cache.Store(cacheScope, request, request.Target, response, responseHeaders, Encoding.ASCII.GetBytes("cached"));
        for (var index = 0; index < warmup; index++)
        {
            cache.Get(cacheScope, request, request.Target);
        }

        var stopwatch = Stopwatch.StartNew();
        CachedProxyResponse? cached = null;
        for (var index = 0; index < operations; index++)
        {
            var lookup = cache.Get(cacheScope, request, request.Target);
            if (lookup is not ProxyCacheLookupResult.HitResult hit)
            {
                throw new InvalidOperationException("Cache hot-path smoke missed a stored response.");
            }

            cached = hit.Response;
        }

        stopwatch.Stop();
        AssertEx.Equal("cached", Encoding.ASCII.GetString(AssertEx.NotNull(cached).Body));
        return Task.FromResult(new PerformanceSmokeResult(Cache, operations, stopwatch.Elapsed, TimeSpan.FromSeconds(5), "cache-safe GET hot path"));
    }

    private static ProxyCacheRequestScope CacheScope(RuntimeRoute route, RuntimeListener listener)
    {
        return new ProxyCacheRequestScope(route.Name, route.Host, listener.Transport == RuntimeListenerTransport.Https ? "https" : "http", new ProxyCachePolicyFacts(route.Cache.Enabled, route.Cache.MaxEntryBytes, route.Cache.MaxTotalBytes, route.Cache.DefaultTtl, route.Cache.RespectOriginCacheControl, route.Cache.VaryByHeaders, route.Cache.CacheableStatusCodes, route.Cache.Methods));
    }

    private static Task<PerformanceSmokeResult> RunHeadersAsync()
    {
        const int warmup = 1_000;
        const int operations = 75_000;
        var policy = new HopByHopHeaderPolicy();
        var headers = new[]
        {
            new ProxyHeaderField("Host", "perf.test"),
            new ProxyHeaderField("Connection", "x-private, keep-alive"),
            new ProxyHeaderField("X-Private", "secret"),
            new ProxyHeaderField("Keep-Alive", "timeout=5"),
            new ProxyHeaderField("Upgrade", "websocket"),
            new ProxyHeaderField("X-One", "1"),
            new ProxyHeaderField("X-Two", "2"),
            new ProxyHeaderField("X-Three", "3"),
            new ProxyHeaderField("Accept", "text/plain"),
            new ProxyHeaderField("User-Agent", "mdrava-perf-smoke")
        };
        for (var index = 0; index < warmup; index++)
        {
            policy.FilterForForwarding(headers, preserveTransferEncoding: false, preserveTrailer: false);
        }

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ProxyHeaderField> filtered = [];
        for (var index = 0; index < operations; index++)
        {
            filtered = policy.FilterForForwarding(headers, preserveTransferEncoding: false, preserveTrailer: false);
        }

        stopwatch.Stop();
        AssertEx.Equal(6, filtered.Count);
        AssertEx.False(filtered.Any(static header => string.Equals(header.Name, "X-Private", StringComparison.OrdinalIgnoreCase)));
        return Task.FromResult(new PerformanceSmokeResult(Headers, operations, stopwatch.Elapsed, TimeSpan.FromSeconds(5), "hop-by-hop and Connection-nominated filtering"));
    }

    private static async Task<TimeSpan> RunHttp1StaticResponseAsync(int operations)
    {
        using var temp = TemporaryDirectory.Create("mdrava-perf-http1-static");
        var proxyPort = GetFreeTcpPort();
        ConfigurationTests.WriteCustomSite(temp.Path, "static.json", StaticSiteJson(proxyPort));
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var warmup = await SendSingleRequestAsync(proxyPort, "GET /static HTTP/1.1\r\nHost: perf.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            AssertEx.True(warmup.Contains("200 OK", StringComparison.Ordinal), warmup);
            var stopwatch = Stopwatch.StartNew();
            for (var index = 0; index < operations; index++)
            {
                var response = await SendSingleRequestAsync(proxyPort, "GET /static HTTP/1.1\r\nHost: perf.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
                AssertEx.True(response.EndsWith("static-ok", StringComparison.Ordinal), response);
            }

            stopwatch.Stop();
            return stopwatch.Elapsed;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<TimeSpan> RunHttp1ProxyAsync(int operations)
    {
        using var temp = TemporaryDirectory.Create("mdrava-perf-http1-proxy");
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        ConfigurationTests.WriteCustomSite(temp.Path, "proxy.json", ProxySiteJson(proxyPort, upstreamPort));
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var upstreamStop = new CancellationTokenSource();
        var upstreamTask = RunReusableHttpUpstreamAsync(upstreamPort, expectedRequests: operations + 1, upstreamStop.Token);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var warmup = await SendSingleRequestAsync(proxyPort, "GET /proxy HTTP/1.1\r\nHost: perf.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            AssertEx.True(warmup.EndsWith("proxied", StringComparison.Ordinal), warmup);
            var stopwatch = Stopwatch.StartNew();
            for (var index = 0; index < operations; index++)
            {
                var response = await SendSingleRequestAsync(proxyPort, "GET /proxy HTTP/1.1\r\nHost: perf.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
                AssertEx.True(response.EndsWith("proxied", StringComparison.Ordinal), response);
            }

            stopwatch.Stop();
            var upstreamRequests = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            AssertEx.Equal(operations + 1, upstreamRequests);
            return stopwatch.Elapsed;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await upstreamStop.CancelAsync().ConfigureAwait(false);
        }
    }

    private static ProxyConfigurationSnapshot CreateSnapshot(int routeCount, bool cacheEnabled)
    {
        var options = new ProxyOptions
        {
            Listeners = [new ListenerOptions
            {
                Name = "main",
                Address = "127.0.0.1",
                Port = 8080
            }

            ]
        };
        for (var index = 0; index < routeCount; index++)
        {
            options.Routes.Add(new ProxyRouteOptions { Name = $"route-{index}", Host = "perf.test", PathPrefix = $"/svc{index}/", Cache = new ProxyCachePolicyOptions { Enabled = cacheEnabled, MaxEntryBytes = 1024 * 1024, MaxTotalBytes = 4 * 1024 * 1024, DefaultTtlSeconds = 60 }, Upstreams = [new UpstreamOptions { Name = "local", Address = "127.0.0.1", Port = 19_000 + index }] });
        }

        var operationalOptions = new ProxyOperationalOptions();
        return ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(options, operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UnixEpoch, "performance-smoke", [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("performance-smoke", "config", "sites", "logs", "certs", "state", "config/proxy.json"), [], [], []));
    }

    private static ProxyConfigurationLoader CreateLoader(string dataDirectory)
    {
        var dataDirectoryProvider = new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory });
        return new ProxyConfigurationLoader(dataDirectoryProvider, new ProxyDataDirectoryBootstrapper(dataDirectoryProvider), new SiteConfigurationParser(), new ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, TimeProvider.System);
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
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.ASCII.GetString(memory.ToArray());
    }

    private static async Task<int> RunReusableHttpUpstreamAsync(int upstreamPort, int expectedRequests, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        using var registration = cancellationToken.Register(static state => ((TcpListener)state!).Stop(), listener);
        var requests = 0;
        try
        {
            while (requests < expectedRequests)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                await ReadHttpRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\n\r\nproxied");
                await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
                requests++;
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException)when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }

        return requests;
    }

    private static async Task ReadHttpRequestHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var previous3 = 0;
        var previous2 = 0;
        var previous1 = 0;
        var buffer = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            var current = buffer[0];
            if (previous3 == '\r' && previous2 == '\n' && previous1 == '\r' && current == '\n')
            {
                return;
            }

            previous3 = previous2;
            previous2 = previous1;
            previous1 = current;
        }
    }

    private static string SiteJson(string name, int port, int upstreamPort)
    {
        return $$"""
        {
          "name": "{{name}}",
          "listeners": [
            {
              "name": "main-{{name}}",
              "address": "127.0.0.1",
              "port": {{port}}
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
        """;
    }

    private static string StaticSiteJson(int proxyPort)
    {
        return $$"""
        {
          "name": "perf-static",
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
              "name": "static",
              "pathPrefix": "/static",
              "action": "staticResponse",
              "staticResponse": {
                "statusCode": 200,
                "contentType": "text/plain",
                "body": "static-ok"
              }
            }
          ]
        }
        """;
    }

    private static string ProxySiteJson(int proxyPort, int upstreamPort)
    {
        return $$"""
        {
          "name": "perf-proxy",
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
              "name": "proxy",
              "pathPrefix": "/proxy",
              "action": "proxy",
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
        """;
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

    private sealed record PerformanceSmokeResult(string Domain, int Operations, TimeSpan Elapsed, TimeSpan Threshold, string Detail)
    {
        public bool Passed => Elapsed <= Threshold;
    }

    private sealed record PerformanceSmokeOptions(IReadOnlySet<string> Domains, bool ListDomains, string? SummaryFile)
    {
        public static PerformanceSmokeOptions Parse(string[] args)
        {
            var performance = false;
            var listDomains = false;
            string? summaryFile = null;
            HashSet<string> domains = new(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];
                if (string.Equals(arg, "--performance", StringComparison.OrdinalIgnoreCase))
                {
                    performance = true;
                    continue;
                }

                if (string.Equals(arg, "--list-performance-domains", StringComparison.OrdinalIgnoreCase))
                {
                    listDomains = true;
                    continue;
                }

                if (string.Equals(arg, "--summary-file", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException($"{arg} requires a file path.");
                    }

                    summaryFile = args[++index];
                    continue;
                }

                if (string.Equals(arg, "--domain", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--domains", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException($"{arg} requires a domain value.");
                    }

                    AddDomains(args[++index], domains);
                    continue;
                }

                const string domainPrefix = "--domain=";
                const string domainsPrefix = "--domains=";
                const string summaryFilePrefix = "--summary-file=";
                if (arg.StartsWith(domainPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddDomains(arg[domainPrefix.Length..], domains);
                    continue;
                }

                if (arg.StartsWith(domainsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddDomains(arg[domainsPrefix.Length..], domains);
                    continue;
                }

                if (arg.StartsWith(summaryFilePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    summaryFile = arg[summaryFilePrefix.Length..];
                    continue;
                }

                throw new ArgumentException($"Unknown performance runner argument: {arg}");
            }

            if (!performance && !listDomains)
            {
                throw new ArgumentException("Performance smoke mode requires --performance.");
            }

            var canonical = domains.Select(CanonicalDomain).OrderBy(static domain => domain, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            return new PerformanceSmokeOptions(canonical, listDomains, summaryFile);
        }

        private static void AddDomains(string value, HashSet<string> domains)
        {
            foreach (var domain in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!IsKnownDomain(domain))
                {
                    throw new ArgumentException($"Unknown performance domain: {domain}");
                }

                domains.Add(domain);
            }
        }

        private static bool IsKnownDomain(string domain)
        {
            return PerformanceSmokeRunner.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase);
        }

        private static string CanonicalDomain(string domain)
        {
            return PerformanceSmokeRunner.Domains.FirstOrDefault(candidate => string.Equals(candidate, domain, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Unknown performance domain: {domain}");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create(string prefix)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
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
