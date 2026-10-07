using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ObservabilityTests
{
    public static void RecentDiagnosticsStoreIsBounded()
    {
        var metrics = new ProxyMetrics();
        var store = new RecentRequestDiagnosticsStore(metrics);
        store.Add(CreateEvent("one", "/one"), capacity: 2);
        store.Add(CreateEvent("two", "/two"), capacity: 2);
        store.Add(CreateEvent("three", "/three"), capacity: 2);
        var recent = store.Recent(10);
        AssertEx.Equal(2, recent.Count);
        AssertEx.Equal("three", recent[0].RequestId);
        AssertEx.Equal("two", recent[1].RequestId);
        AssertEx.False(recent is List<ProxyRecentRequestDiagnosticEvent>, "Recent diagnostics should not expose a mutable list.");
        AssertEx.Equal(1L, metrics.Snapshot().Diagnostics.RecentDiagnosticsOverwritten);
    }

    public static void DiagnosticsControllerHonorsSafeLimit()
    {
        var metrics = new ProxyMetrics();
        var store = new RecentRequestDiagnosticsStore(metrics);
        for (var index = 0; index < 10; index++)
        {
            store.Add(CreateEvent($"request-{index}", $"/{index}"), capacity: 20);
        }

        var controller = new ProxyDiagnosticsController(new ProxyDiagnosticsAdministrationService(CreateRequestDiagnosticsReader(store)));
        var recent = controller.Recent(limit: 3);
        AssertEx.Equal(3, recent.Count);
        AssertEx.Equal("request-9", recent[0].RequestId);
        AssertEx.False(recent is ProxyRecentRequestDiagnosticEventResponse[], "Diagnostics API recent events should not expose a mutable array.");
    }

    public static void RequestDiagnosticsReaderReturnsRecentEventsFromSource()
    {
        var source = new FixedRequestDiagnosticsSource([CreateSourceEvent("new", "/new"), CreateSourceEvent("old", "/old")]);
        var reader = new ProxyRequestDiagnosticsReader(source);
        var recent = reader.Recent(limit: 7);
        AssertEx.Equal(7, source.LastLimit);
        AssertEx.Equal(2, recent.Count);
        AssertEx.Equal("new", recent[0].RequestId);
        AssertEx.Equal("/new", recent[0].Target);
        AssertEx.Equal("client-new", recent[0].ExternalRequestId);
        AssertEx.Equal(2, recent[0].ConfigVersion);
        AssertEx.Equal("quic", recent[0].Transport);
        AssertEx.Equal("10.0.0.1:12345", recent[0].ClientEndpoint);
        AssertEx.Equal("POST", recent[0].Method);
        AssertEx.Equal("example.test", recent[0].Host);
        AssertEx.Equal("home", recent[0].RouteName);
        AssertEx.Equal("upstream", recent[0].UpstreamName);
        AssertEx.Equal("127.0.0.1:5000", recent[0].UpstreamEndpoint);
        AssertEx.Equal(502, recent[0].ResponseStatusCode);
        AssertEx.Equal(42L, recent[0].DurationMilliseconds);
        AssertEx.Equal("UpstreamConnectFailed", recent[0].FailureKind);
        AssertEx.True(recent[0].ResponseStarted);
        AssertEx.True(recent[0].KeepClientConnectionOpen);
        AssertEx.True(recent[0].IsUpgrade);
        AssertEx.True(recent[0].TunnelEstablished);
        AssertEx.Equal("client_closed", recent[0].TunnelCloseReason);
        AssertEx.Equal(11L, recent[0].TunnelBytesClientToUpstream);
        AssertEx.Equal(12L, recent[0].TunnelBytesUpstreamToClient);
        AssertEx.Equal("old", recent[1].RequestId);
    }

    public static void DiagnosticsEventDoesNotCarryBodiesOrSecrets()
    {
        var diagnostic = CreateEvent("request", "/secret");
        AssertEx.Equal("/secret", diagnostic.Target);
        AssertEx.False(diagnostic.ToString().Contains("request-body", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(diagnostic.ToString().Contains("certificate-password", StringComparison.OrdinalIgnoreCase));
    }

    public static void ExternalRequestIdPolicyNormalizesSafeHeaderValues()
    {
        var validHead = new Http1RequestHead("GET", "/", "/", "HTTP/1.1", "example.test", Http1RequestFraming.None, [new ProxyHeaderField("X-Request-Id", "  client-123  ")]);
        var controlHead = new Http1RequestHead("GET", "/", "/", "HTTP/1.1", "example.test", Http1RequestFraming.None, [new ProxyHeaderField("X-Request-Id", "client\r123")]);
        var tooLongHead = new Http1RequestHead("GET", "/", "/", "HTTP/1.1", "example.test", Http1RequestFraming.None, [new ProxyHeaderField("X-Request-Id", new string ('a', 129))]);
        AssertEx.Equal("client-123", ProxyExternalRequestIdPolicy.Extract(validHead));
        AssertEx.True(ProxyExternalRequestIdPolicy.Extract(controlHead)is null);
        AssertEx.True(ProxyExternalRequestIdPolicy.Extract(tooLongHead)is null);
    }

    public static void RequestIdGeneratorCreatesDistinctIdsAndRecordsMetric()
    {
        var metrics = new RequestIdMetricsSink();
        var generator = new RequestIdGenerator(metrics, new FixedRequestIdRuntimeIdentitySource());
        var first = generator.Create();
        var second = generator.Create();
        AssertEx.True(first.StartsWith("mdr-test-runtime-", StringComparison.Ordinal), first);
        AssertEx.True(second.StartsWith("mdr-test-runtime-", StringComparison.Ordinal), second);
        AssertEx.False(string.Equals(first, second, StringComparison.Ordinal));
        AssertEx.Equal(2, metrics.GeneratedCount);
    }

    private static ProxyRecentRequestDiagnosticEvent CreateEvent(string requestId, string target)
    {
        return new ProxyRecentRequestDiagnosticEvent(DateTimeOffset.UtcNow, requestId, null, 1, "main", "Http", "127.0.0.1:12345", "GET", "example.test", target, "home", "local-test", "127.0.0.1:5000", 200, 10, ProxyFailureKind.None.ToString(), true, false, false, false, null, 0, 0);
    }

    private static ProxyRequestDiagnosticsReader CreateRequestDiagnosticsReader(RecentRequestDiagnosticsStore store)
    {
        return new ProxyRequestDiagnosticsReader(store);
    }

    private static ProxyRecentRequestDiagnosticEvent CreateSourceEvent(string requestId, string target)
    {
        return new ProxyRecentRequestDiagnosticEvent(DateTimeOffset.UnixEpoch.AddSeconds(string.Equals(requestId, "new", StringComparison.Ordinal) ? 2 : 1), requestId, $"client-{requestId}", 2, "main", "quic", "10.0.0.1:12345", "POST", "example.test", target, "home", "upstream", "127.0.0.1:5000", 502, 42, "UpstreamConnectFailed", true, true, true, true, "client_closed", 11, 12);
    }

    private sealed class FixedRequestDiagnosticsSource : IProxyRequestDiagnosticsSource
    {
        private readonly IReadOnlyList<ProxyRecentRequestDiagnosticEvent> _events;
        public FixedRequestDiagnosticsSource(IReadOnlyList<ProxyRecentRequestDiagnosticEvent> events)
        {
            _events = events;
        }

        public int LastLimit { get; private set; }

        public IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Recent(int limit)
        {
            LastLimit = limit;
            return _events;
        }
    }

    private sealed class RequestIdMetricsSink : IProxyRequestIdMetricsSink
    {
        public int GeneratedCount { get; private set; }

        public void RequestIdGenerated()
        {
            GeneratedCount++;
        }
    }

    private sealed class FixedRequestIdRuntimeIdentitySource : IProxyRequestIdRuntimeIdentitySource
    {
        public string RuntimeIdentity => "test-runtime";
    }
}
