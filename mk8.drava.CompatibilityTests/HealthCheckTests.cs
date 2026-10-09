using System.Net;
using System.Net.Sockets;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class HealthCheckTests
{
    public static async Task HealthCheck2xxIsHealthyAsync()
    {
        var sample = await RunHealthCheckAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n").ConfigureAwait(false);
        AssertEx.True(sample.Healthy, sample.Result);
    }

    public static async Task HealthCheck3xxIsHealthyAsync()
    {
        var sample = await RunHealthCheckAsync("HTTP/1.1 302 Found\r\nContent-Length: 0\r\n\r\n").ConfigureAwait(false);
        AssertEx.True(sample.Healthy, sample.Result);
    }

    public static async Task HealthCheck4xxIsUnhealthyAsync()
    {
        var sample = await RunHealthCheckAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n").ConfigureAwait(false);
        AssertEx.False(sample.Healthy, sample.Result);
    }

    public static async Task HealthCheck5xxIsUnhealthyAsync()
    {
        var sample = await RunHealthCheckAsync("HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\n\r\n").ConfigureAwait(false);
        AssertEx.False(sample.Healthy, sample.Result);
    }

    public static async Task HealthCheckTimeoutIsUnhealthyAsync()
    {
        var port = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstream = Upstream(port);
        var route = Route([upstream], timeoutSeconds: 1);
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            var acceptTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(1500), timeout.Token).ConfigureAwait(false);
            }, timeout.Token);
            var sample = await new UpstreamHealthCheckClient(new UpstreamConnectionFactory(), new ProxyMetrics()).CheckAsync(Target(route, upstream), timeout.Token).ConfigureAwait(false);
            AssertEx.False(sample.Healthy, sample.Result);
            AssertEx.True(sample.Result.Contains("timeout", StringComparison.OrdinalIgnoreCase), sample.Result);
            await acceptTask.ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    public static void RuntimeTimeoutsFactoryBuildsHealthCheckTimeouts()
    {
        var timeout = TimeSpan.FromSeconds(7);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(timeout);
        AssertEx.Equal(timeout, timeouts.ClientRequestHeadTimeout);
        AssertEx.Equal(timeout, timeouts.ClientRequestBodyIdleTimeout);
        AssertEx.Equal(timeout, timeouts.UpstreamConnectTimeout);
        AssertEx.Equal(timeout, timeouts.UpstreamResponseHeadTimeout);
        AssertEx.Equal(timeout, timeouts.UpstreamResponseBodyIdleTimeout);
        AssertEx.Equal(timeout, timeouts.DownstreamWriteTimeout);
        AssertEx.Equal(timeout, timeouts.TlsHandshakeTimeout);
        AssertEx.Equal(timeout, timeouts.ClientKeepAliveIdleTimeout);
        AssertEx.Equal(timeout, timeouts.UpstreamIdleConnectionLifetime);
        AssertEx.Equal(timeout, timeouts.TunnelIdleTimeout);
    }

    public static void HealthStateTransitionsToUnhealthyAfterThreshold()
    {
        var metrics = new ProxyMetrics();
        using var pool = new UpstreamConnectionPool(new UpstreamConnectionFactory(), metrics, TimeProvider.System);
        var circuit = new CircuitBreakerStore(metrics, TimeProvider.System);
        var store = new UpstreamHealthStore(metrics, pool, circuit);
        var upstream = Upstream(5001);
        var route = Route([upstream], unhealthyThreshold: 2);
        var target = Target(route, upstream);
        store.RecordHealthCheckResult(target, HealthCheckSample.UnhealthyResult("fail"), DateTimeOffset.UtcNow);
        AssertEx.True(store.IsUsable(HealthSource(upstream)));
        var state = store.RecordHealthCheckResult(target, HealthCheckSample.UnhealthyResult("fail"), DateTimeOffset.UtcNow);
        AssertEx.Equal(UpstreamHealthState.Unhealthy, state);
        AssertEx.False(store.IsUsable(HealthSource(upstream)));
    }

    public static void HealthStateTransitionsToHealthyAfterRecoveryThreshold()
    {
        var metrics = new ProxyMetrics();
        using var pool = new UpstreamConnectionPool(new UpstreamConnectionFactory(), metrics, TimeProvider.System);
        var circuit = new CircuitBreakerStore(metrics, TimeProvider.System);
        var store = new UpstreamHealthStore(metrics, pool, circuit);
        var upstream = Upstream(5001);
        var route = Route([upstream], healthyThreshold: 2, unhealthyThreshold: 1);
        var target = Target(route, upstream);
        store.RecordHealthCheckResult(target, HealthCheckSample.UnhealthyResult("fail"), DateTimeOffset.UtcNow);
        AssertEx.False(store.IsUsable(HealthSource(upstream)));
        store.RecordHealthCheckResult(target, HealthCheckSample.HealthyResult("ok"), DateTimeOffset.UtcNow);
        AssertEx.False(store.IsUsable(HealthSource(upstream)));
        var state = store.RecordHealthCheckResult(target, HealthCheckSample.HealthyResult("ok"), DateTimeOffset.UtcNow);
        AssertEx.Equal(UpstreamHealthState.Healthy, state);
        AssertEx.True(store.IsUsable(HealthSource(upstream)));
    }

    public static async Task HealthCheckCoordinatorRunsDueChecksAndRecordsMetricsAsync()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var metrics = new ProxyMetrics();
        using var pool = new UpstreamConnectionPool(new UpstreamConnectionFactory(), metrics, clock);
        var circuit = new CircuitBreakerStore(metrics, clock);
        var store = new UpstreamHealthStore(metrics, pool, circuit);
        var upstream = Upstream(5001);
        var route = Route([upstream], healthyThreshold: 1);
        var client = new FixedHealthCheckClient(HealthCheckSample.HealthyResult("ok"));
        var events = new CapturingHealthCheckEventSink();
        var coordinator = new UpstreamHealthCheckCoordinator(client, store, metrics, clock, events);
        await coordinator.RunDueChecksAsync(Targets(route), CancellationToken.None).ConfigureAwait(false);
        AssertEx.Equal(1, client.Calls);
        var counters = metrics.Snapshot();
        AssertEx.Equal(1L, counters.Health.ChecksAttempted);
        AssertEx.Equal(1L, counters.Health.ChecksSucceeded);
        AssertEx.Equal(0L, counters.Health.ChecksFailed);
        AssertEx.True(store.IsUsable(HealthSource(upstream)));
        AssertEx.Equal(1, events.Events.Count);
        AssertEx.Equal(UpstreamHealthState.Healthy, events.Events[0].State);
    }

    public static async Task HealthCheckCoordinatorSkipsUntilIntervalElapsesAsync()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var metrics = new ProxyMetrics();
        using var pool = new UpstreamConnectionPool(new UpstreamConnectionFactory(), metrics, clock);
        var circuit = new CircuitBreakerStore(metrics, clock);
        var store = new UpstreamHealthStore(metrics, pool, circuit);
        var upstream = Upstream(5001);
        var route = Route([upstream]);
        var client = new FixedHealthCheckClient(HealthCheckSample.UnhealthyResult("fail"));
        var coordinator = new UpstreamHealthCheckCoordinator(client, store, metrics, clock, new CapturingHealthCheckEventSink());
        var targets = Targets(route);
        await coordinator.RunDueChecksAsync(targets, CancellationToken.None).ConfigureAwait(false);
        await coordinator.RunDueChecksAsync(targets, CancellationToken.None).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(2));
        await coordinator.RunDueChecksAsync(targets, CancellationToken.None).ConfigureAwait(false);
        AssertEx.Equal(2, client.Calls);
        var counters = metrics.Snapshot();
        AssertEx.Equal(2L, counters.Health.ChecksAttempted);
        AssertEx.Equal(2L, counters.Health.ChecksFailed);
    }

    public static void HealthCheckTargetSourceReadsOnlyMappedActiveTargets()
    {
        var enabledUpstream = Upstream(5001);
        var disabledUpstream = Upstream(5002);
        var store = new ProxyConfigurationStore();
        store.Replace(Snapshot([Route([enabledUpstream]), Route([disabledUpstream], healthEnabled: false)]));
        var source = new ProxyConfigurationUpstreamHealthCheckTargetSource(store);
        var targets = source.ReadTargets();
        AssertEx.Equal(1, targets.Count);
        AssertEx.Equal("test", targets[0].RouteName);
        AssertEx.Equal(enabledUpstream.Identity, targets[0].UpstreamIdentity);
        AssertEx.Equal(enabledUpstream.Name, targets[0].UpstreamName);
        AssertEx.Equal(enabledUpstream.Endpoint, targets[0].UpstreamEndpoint);
        AssertEx.Equal(enabledUpstream.Address, targets[0].TransportEndpoint.Address);
        AssertEx.Equal("/health", targets[0].Path);
    }

    public static void HealthCheckTargetMapperReadsRoutesWithoutConfigurationSnapshot()
    {
        AssertEx.Throws<ArgumentNullException>(() => UpstreamHealthCheckTargetMapper.FromRoutes(null!));
        AssertEx.Throws<ArgumentNullException>(() => UpstreamHealthCheckTargetMapper.FromRoutes([null!]));
        AssertEx.Throws<ArgumentNullException>(() => UpstreamHealthCheckTargetMapper.FromRoutes([Route([null!])]));
        AssertEx.Throws<ArgumentNullException>(() => UpstreamTransportEndpointMapper.FromUpstream(null!));
        var first = Upstream(5001);
        var second = Upstream(5002);
        var routes = new[]
        {
            Route([first, second], timeoutSeconds: 3, healthyThreshold: 1, unhealthyThreshold: 4),
            Route([Upstream(5003)], healthEnabled: false)
        };
        var targets = UpstreamHealthCheckTargetMapper.FromRoutes(routes.Select(static route => route));
        AssertEx.Equal(2, targets.Count);
        AssertEx.Equal("test", targets[0].RouteName);
        AssertEx.Equal(first.Name, targets[0].UpstreamName);
        AssertEx.Equal(first.Identity, targets[0].UpstreamIdentity);
        AssertEx.Equal(first.Endpoint, targets[0].UpstreamEndpoint);
        AssertEx.Equal(TimeSpan.FromSeconds(3), targets[0].Timeout);
        AssertEx.Equal(1, targets[0].HealthyThreshold);
        AssertEx.Equal(4, targets[0].UnhealthyThreshold);
        AssertEx.Equal(second.Name, targets[1].UpstreamName);
        AssertEx.False(targets is UpstreamHealthCheckTarget[], "Health check targets should not expose a mutable array.");
    }

    public static void UpstreamHealthSourceMapperReadsRoutesWithoutConfigurationSnapshot()
    {
        AssertEx.Throws<ArgumentNullException>(() => ProxyUpstreamHealthSourceMapper.FromRoutes(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyUpstreamHealthSourceMapper.FromRoutes([null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyUpstreamHealthSourceMapper.FromRoutes([Route([null!])]));
        AssertEx.Throws<ArgumentNullException>(() => UpstreamHealthStateSourceMapper.FromUpstream(null!));
        AssertEx.Throws<ArgumentNullException>(() => CircuitBreakerStatusSourceMapper.FromUpstream(null!));
        var http = Upstream(5001);
        var https = new RuntimeUpstream("test", "secure", "https", RuntimeUpstreamProtocol.Http2, "secure.internal", 443, 7, new RuntimeUpstreamTlsOptions(false, "sni.internal"));
        var routes = new[]
        {
            Route([http]),
            Route([https], healthEnabled: false)
        };
        var sources = ProxyUpstreamHealthSourceMapper.FromRoutes(routes.Select(static route => route));
        AssertEx.Equal(2, sources.Count);
        AssertEx.Equal(http.Identity, sources[0].HealthState.UpstreamIdentity);
        AssertEx.Equal(http.RouteName, sources[0].HealthState.RouteName);
        AssertEx.Equal(http.Name, sources[0].HealthState.UpstreamName);
        AssertEx.Equal(http.Endpoint, sources[0].HealthState.UpstreamEndpoint);
        AssertEx.Equal("http", sources[0].Scheme);
        AssertEx.False(sources[0].ValidateCertificate);
        AssertEx.True(sources[0].HealthCheckEnabled);
        AssertEx.Equal(https.Identity, sources[1].HealthState.UpstreamIdentity);
        AssertEx.Equal(RuntimeUpstreamProtocol.Http2, sources[1].Protocol);
        AssertEx.Equal(7, sources[1].Weight);
        AssertEx.False(sources[1].ValidateCertificate);
        AssertEx.Equal("sni.internal", sources[1].EffectiveSniHost);
        AssertEx.False(sources[1].HealthCheckEnabled);
        AssertEx.False(sources is ProxyUpstreamHealthSource[], "Upstream health sources should not expose a mutable array.");
    }

    public static void HealthCheckSampleNamesStatusesAndExplicitResults()
    {
        var healthy = HealthCheckSample.HealthyResult("ok");
        var unhealthy = HealthCheckSample.UnhealthyResult("failed");
        var httpHealthy = HealthCheckSample.FromHttpStatus(302);
        var httpUnhealthy = HealthCheckSample.FromHttp2Status(500);
        var http3Healthy = HealthCheckSample.FromHttp3Status(204);
        AssertEx.True(healthy.Healthy);
        AssertEx.Equal("ok", healthy.Result);
        AssertEx.False(unhealthy.Healthy);
        AssertEx.Equal("failed", unhealthy.Result);
        AssertEx.True(httpHealthy.Healthy);
        AssertEx.Equal("HTTP 302", httpHealthy.Result);
        AssertEx.False(httpUnhealthy.Healthy);
        AssertEx.Equal("HTTP/2 500", httpUnhealthy.Result);
        AssertEx.True(http3Healthy.Healthy);
        AssertEx.Equal("HTTP/3 204", http3Healthy.Result);
    }

    private static async Task<HealthCheckSample> RunHealthCheckAsync(string response)
    {
        var port = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstream = Upstream(port);
        var route = Route([upstream]);
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                await ReadRequestHeadAsync(stream, timeout.Token).ConfigureAwait(false);
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(response), timeout.Token).ConfigureAwait(false);
            }, timeout.Token);
            var sample = await new UpstreamHealthCheckClient(new UpstreamConnectionFactory(), new ProxyMetrics()).CheckAsync(Target(route, upstream), timeout.Token).ConfigureAwait(false);
            await serverTask.ConfigureAwait(false);
            return sample;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static RuntimeRoute Route(IReadOnlyList<RuntimeUpstream> upstreams, int timeoutSeconds = 1, int healthyThreshold = 2, int unhealthyThreshold = 2, bool healthEnabled = true)
    {
        return new RuntimeRoute("test", "*", "/", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(healthEnabled, "/health", TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)), TimeSpan.FromSeconds(timeoutSeconds), healthyThreshold, unhealthyThreshold), upstreams, new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", ""), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), RuntimeCachePolicy.Disabled, new RuntimeRouteResolvedOptions(100L * 1024 * 1024, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true));
    }

    private static ProxyConfigurationSnapshot Snapshot(IReadOnlyList<RuntimeRoute> routes)
    {
        return new ProxyConfigurationSnapshot(1, DateTimeOffset.UtcNow, "tests", [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []), new RuntimeAdminSecurityOptions([], false, false, null, "MDRAVA_ADMIN_TOKEN", "none", 100), new RuntimeAcmeOptions(false, true, "", [], false, "acme", 30, 720, 60, []), new RuntimeTimeouts(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)), new RuntimeConnectionLimits(100, 16, 1024), new RuntimeObservabilityOptions(true, 100, new RuntimeLogPersistenceOptions(true, true, 1_048_576, 8)), new RuntimeLimits(4096, 128, 240, 30, 32768, 128, 8192, 104857600, 8192, TimeSpan.FromSeconds(15)), new RuntimeForwardedHeadersOptions(true, []), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), [], routes);
    }

    private static UpstreamHealthCheckTarget[] Targets(RuntimeRoute route)
    {
        return route.Upstreams.Select(upstream => Target(route, upstream)).ToArray();
    }

    private static UpstreamHealthCheckTarget Target(RuntimeRoute route, RuntimeUpstream upstream)
    {
        return new UpstreamHealthCheckTarget(route.Name, upstream.Name, upstream.Endpoint, upstream.Identity, UpstreamTransportEndpointMapper.FromUpstream(upstream), route.HealthCheck.Path, route.HealthCheck.Interval, route.HealthCheck.Timeout, route.HealthCheck.HealthyThreshold, route.HealthCheck.UnhealthyThreshold);
    }

    private static RuntimeUpstream Upstream(int port)
    {
        return new RuntimeUpstream("test", $"upstream-{port}", "http", RuntimeUpstreamProtocol.Http1, "127.0.0.1", port, 1, RuntimeUpstreamTlsOptions.Default);
    }

    private static UpstreamHealthStateSource HealthSource(RuntimeUpstream upstream)
    {
        return UpstreamHealthStateSourceMapper.FromUpstream(upstream);
    }

    private static async Task ReadRequestHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        var total = 0;
        while (total < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(total, 1), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return;
            }

            total += bytesRead;
            if (total >= 4 && buffer[total - 4] == '\r' && buffer[total - 3] == '\n' && buffer[total - 2] == '\r' && buffer[total - 1] == '\n')
            {
                return;
            }
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
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

    private sealed class FixedHealthCheckClient : IUpstreamHealthCheckClient
    {
        private readonly HealthCheckSample _sample;
        public FixedHealthCheckClient(HealthCheckSample sample)
        {
            _sample = sample;
        }

        public int Calls { get; private set; }

        public ValueTask<HealthCheckSample> CheckAsync(UpstreamHealthCheckTarget target, CancellationToken cancellationToken)
        {
            _ = target;
            _ = cancellationToken;
            Calls++;
            return ValueTask.FromResult(_sample);
        }
    }

    private sealed class CapturingHealthCheckEventSink : IUpstreamHealthCheckEventSink
    {
        public List<HealthCheckEvent> Events { get; } = [];

        public void Checked(string routeName, string upstreamName, string endpoint, string result, UpstreamHealthState state)
        {
            Events.Add(new HealthCheckEvent(routeName, upstreamName, endpoint, result, state));
        }
    }

    private sealed record HealthCheckEvent(string RouteName, string UpstreamName, string Endpoint, string Result, UpstreamHealthState State);
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan delay)
        {
            _utcNow += delay;
        }
    }
}
