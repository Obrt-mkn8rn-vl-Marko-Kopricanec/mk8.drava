using System.Net;
using System.Net.Sockets;
using System.Text;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ResilienceTests
{
    public static async Task ExistingBehaviorUnchangedWhenResilienceDisabledAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteResilienceSite(temp.Path, proxyPort, [new TestUpstream("first", upstreamPort)]);
        var upstreamTask = RunCountingUpstreamAsync(upstreamPort, 1, _ => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\n\r\ndefault", timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, "GET /default HTTP/1.1\r\nHost: resilience.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            var requests = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            AssertEx.Equal(1, requests.Count);
            AssertEx.Equal(0L, metrics.Resilience.RetryAttempts);
            AssertEx.Equal(0L, metrics.Resilience.CircuitOpened);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task GetRetryOccursOnConnectFailureWhenEnabledAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var closedPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteResilienceSite(temp.Path, proxyPort, [new TestUpstream("first", closedPort), new TestUpstream("second", upstreamPort)], RetryJson(maxAttempts: 2, retryOnConnectFailure: true));
        var upstreamTask = RunCountingUpstreamAsync(upstreamPort, 1, _ => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\n\r\nretried", timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, "GET /retry HTTP/1.1\r\nHost: resilience.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            var requests = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            AssertEx.Equal(1, requests.Count);
            AssertEx.Equal(1L, metrics.Resilience.RetryAttempts);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task GetRetryOccursOnConfiguredStatusWhenEnabledAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var firstPort = GetFreeTcpPort();
        var secondPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteResilienceSite(temp.Path, proxyPort, [new TestUpstream("first", firstPort), new TestUpstream("second", secondPort)], RetryStatusJson(maxAttempts: 2, statusCode: 503));
        var firstTask = RunCountingUpstreamAsync(firstPort, 1, _ => "HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\nContent-Length: 5\r\n\r\nerror", timeout.Token);
        var secondTask = RunCountingUpstreamAsync(secondPort, 1, _ => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\n\r\nsuccess", timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, "GET /status HTTP/1.1\r\nHost: resilience.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            var firstRequests = await firstTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var secondRequests = await secondTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            AssertEx.True(response.EndsWith("success", StringComparison.Ordinal), response);
            AssertEx.Equal(1, firstRequests.Count);
            AssertEx.Equal(1, secondRequests.Count);
            AssertEx.Equal(1L, metrics.Resilience.RetryAttempts);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task PostIsNotRetriedByDefaultAsync()
    {
        var result = await RunClosedUpstreamScenarioAsync("POST /retry HTTP/1.1\r\nHost: resilience.test\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", RetryJson(maxAttempts: 2, retryOnConnectFailure: true)).ConfigureAwait(false);
        AssertEx.True(result.Response.Contains("502 Bad Gateway", StringComparison.Ordinal), result.Response);
        AssertEx.Equal(0L, result.Metrics.Resilience.RetryAttempts);
        AssertEx.True(result.Metrics.Resilience.RetrySkipped.Any(static item => string.Equals(item.Reason, "method", StringComparison.Ordinal)));
    }

    public static async Task UpgradeIsNotRetriedAsync()
    {
        var result = await RunClosedUpstreamScenarioAsync("GET /chat HTTP/1.1\r\nHost: resilience.test\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n", RetryJson(maxAttempts: 2, retryOnConnectFailure: true)).ConfigureAwait(false);
        AssertEx.True(result.Response.Contains("502 Bad Gateway", StringComparison.Ordinal), result.Response);
        AssertEx.Equal(0L, result.Metrics.Resilience.RetryAttempts);
    }

    public static async Task RequestIsNotRetriedAfterResponseStreamingStartsAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteResilienceSite(temp.Path, proxyPort, [new TestUpstream("first", upstreamPort)], RetryJson(maxAttempts: 2, retryOnConnectFailure: true));
        var upstreamTask = RunCountingUpstreamAsync(upstreamPort, 1, _ => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 10\r\n\r\npart", timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, "GET /partial HTTP/1.1\r\nHost: resilience.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            var requests = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            AssertEx.True(response.EndsWith("part", StringComparison.Ordinal), response);
            AssertEx.Equal(1, requests.Count);
            AssertEx.Equal(0L, metrics.Resilience.RetryAttempts);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task PartialResponseFailureDoesNotRetrySecondUpstreamAfterDownstreamBytesAreSentAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var firstPort = GetFreeTcpPort();
        var closedSecondPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteResilienceSite(temp.Path, proxyPort, [new TestUpstream("first", firstPort), new TestUpstream("second", closedSecondPort)], RetryJson(maxAttempts: 2, retryOnConnectFailure: true));
        var firstTask = RunCountingUpstreamAsync(firstPort, 1, _ => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 12\r\n\r\npartial", timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, "GET /partial HTTP/1.1\r\nHost: resilience.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            var firstRequests = await firstTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            AssertEx.True(response.EndsWith("partial", StringComparison.Ordinal), response);
            AssertEx.Equal(1, firstRequests.Count);
            AssertEx.Equal(0L, metrics.Resilience.RetryAttempts);
            AssertEx.Equal(1L, metrics.UpstreamForwarding.BodyRelayFailures);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task RetryStatusDoesNotBypassUnsafePostMethodAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var firstPort = GetFreeTcpPort();
        var closedSecondPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteResilienceSite(temp.Path, proxyPort, [new TestUpstream("first", firstPort), new TestUpstream("second", closedSecondPort)], RetryStatusJson(maxAttempts: 2, statusCode: 503));
        var firstTask = RunCountingUpstreamAsync(firstPort, 1, _ => "HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\nContent-Length: 5\r\n\r\nerror", timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, "POST /status HTTP/1.1\r\nHost: resilience.test\r\nContent-Length: 4\r\nConnection: close\r\n\r\nbody", timeout.Token).ConfigureAwait(false);
            var firstRequests = await firstTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            AssertEx.True(response.Contains("503 Service Unavailable", StringComparison.Ordinal), response);
            AssertEx.Equal(1, firstRequests.Count);
            AssertEx.Equal(0L, metrics.Resilience.RetryAttempts);
            AssertEx.True(metrics.Resilience.RetrySkipped.Any(static item => string.Equals(item.Reason, "method", StringComparison.Ordinal)));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task RetryMaxAttemptsIsEnforcedAsync()
    {
        var result = await RunClosedUpstreamScenarioAsync("GET /retry HTTP/1.1\r\nHost: resilience.test\r\nConnection: close\r\n\r\n", RetryJson(maxAttempts: 3, retryOnConnectFailure: true)).ConfigureAwait(false);
        AssertEx.True(result.Response.Contains("502 Bad Gateway", StringComparison.Ordinal), result.Response);
        AssertEx.Equal(2L, result.Metrics.Resilience.RetryAttempts);
        AssertEx.Equal(1L, result.Metrics.Resilience.RetryExhausted);
    }

    public static async Task RetryExhaustedReturnsClearFailureAsync()
    {
        var result = await RunClosedUpstreamScenarioAsync("GET /retry HTTP/1.1\r\nHost: resilience.test\r\nConnection: close\r\n\r\n", RetryJson(maxAttempts: 2, retryOnConnectFailure: true)).ConfigureAwait(false);
        AssertEx.True(result.Response.Contains("HTTP/1.1 502 Bad Gateway", StringComparison.Ordinal), result.Response);
        AssertEx.True(result.Response.EndsWith("Bad Gateway", StringComparison.Ordinal), result.Response);
        AssertEx.Equal(1L, result.Metrics.Resilience.RetryExhausted);
    }

    public static void CircuitOpensAfterThresholdFailures()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 2))]);
        var first = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(first.CircuitBreakerLease, "connect_failure");
        var second = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(second.CircuitBreakerLease, "connect_failure");
        AssertEx.Equal(CircuitBreakerRuntimeState.Open, fixture.Circuit.Snapshot(StatusSource(route.Upstreams[0])).State);
        AssertEx.Throws<ArgumentNullException>(() => new CircuitBreakerStatusSource(null!, CircuitBreakerPolicyInput.Disabled));
        AssertEx.Throws<ArgumentNullException>(() => new CircuitBreakerStatusSource("route/upstream", null!));
        AssertEx.Throws<ArgumentNullException>(() => fixture.Circuit.IsAvailable(null!));
        AssertEx.Throws<ArgumentNullException>(() => fixture.Circuit.RecordRejectedIfUnavailable(null!));
        AssertEx.Throws<ArgumentNullException>(() => fixture.Circuit.Acquire(null!));
        AssertEx.Throws<ArgumentNullException>(() => fixture.Circuit.Snapshot(null!));
    }

    public static void UpstreamAttemptRecorderRecordsConfiguredStatusFailuresWithoutHealthFailure()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 1, failureStatusCodes: [503]))]);
        var selection = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        ProxyUpstreamAttemptRecorder.Record(selection, ForwardingResult.Success(responseStarted: true, keepClientConnectionOpen: false, responseStatusCode: 503), fixture.Health, fixture.Circuit);
        AssertEx.Equal(CircuitBreakerRuntimeState.Open, fixture.Circuit.Snapshot(StatusSource(route.Upstreams[0])).State);
        AssertEx.Equal(1L, fixture.Metrics.Snapshot().Resilience.CircuitOpened);
        AssertEx.Equal(0L, fixture.Metrics.Snapshot().UpstreamFailureReasons.RequestFailures);
    }

    public static void UpstreamAttemptRecorderRecordsNonCircuitFailuresWithoutOpeningCircuit()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 1))]);
        var selection = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        ProxyUpstreamAttemptRecorder.Record(selection, ForwardingResult.Failure(responseStarted: false, responseStatusCode: 502, failureKind: ProxyFailureKind.UpstreamMalformedResponse), fixture.Health, fixture.Circuit);
        AssertEx.Equal(CircuitBreakerRuntimeState.Closed, fixture.Circuit.Snapshot(StatusSource(route.Upstreams[0])).State);
        AssertEx.Equal(0L, fixture.Metrics.Snapshot().Resilience.CircuitOpened);
        AssertEx.Equal(1L, fixture.Metrics.Snapshot().UpstreamFailureReasons.RequestFailures);
    }

    public static void CircuitRejectsTrafficWhileOpen()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 1))]);
        var first = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(first.CircuitBreakerLease, "connect_failure");
        var second = fixture.Selector.Select(SelectionRoute(route));
        AssertEx.Equal(null, second);
        AssertEx.Equal(1L, fixture.Metrics.Snapshot().Resilience.CircuitRejections);
    }

    public static void CircuitTransitionsToHalfOpenAfterOpenDuration()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 1, openSeconds: 5))]);
        var first = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(first.CircuitBreakerLease, "connect_failure");
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var second = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        AssertEx.True(AssertEx.NotNull(second.CircuitBreakerLease).HalfOpenProbe);
        AssertEx.Equal(CircuitBreakerRuntimeState.HalfOpen, fixture.Circuit.Snapshot(StatusSource(route.Upstreams[0])).State);
    }

    public static void HalfOpenProbeCountIsBounded()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 1, openSeconds: 1))]);
        var first = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(first.CircuitBreakerLease, "connect_failure");
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var probe = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        var rejected = fixture.Selector.Select(SelectionRoute(route));
        AssertEx.True(AssertEx.NotNull(probe.CircuitBreakerLease).HalfOpenProbe);
        AssertEx.Equal(null, rejected);
        AssertEx.Equal(1L, fixture.Metrics.Snapshot().Resilience.CircuitRejections);
    }

    public static void HalfOpenSuccessClosesCircuit()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 1, openSeconds: 1))]);
        var first = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(first.CircuitBreakerLease, "connect_failure");
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var probe = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordSuccess(probe.CircuitBreakerLease);
        AssertEx.Equal(CircuitBreakerRuntimeState.Closed, fixture.Circuit.Snapshot(StatusSource(route.Upstreams[0])).State);
    }

    public static void HalfOpenFailureReopensCircuit()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 1, openSeconds: 1))]);
        var first = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(first.CircuitBreakerLease, "connect_failure");
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var probe = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(probe.CircuitBreakerLease, "connect_failure");
        AssertEx.Equal(CircuitBreakerRuntimeState.Open, fixture.Circuit.Snapshot(StatusSource(route.Upstreams[0])).State);
    }

    public static void WeightedRoundRobinHonorsWeights()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 2), Upstream("second", weight: 1)]);
        var selected = new[]
        {
            AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route))).Upstream.Name,
            AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route))).Upstream.Name,
            AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route))).Upstream.Name
        };
        AssertEx.Equal("first,first,second", string.Join(',', selected));
    }

    public static void EqualWeightRoundRobinPreservesExistingOrder()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1), Upstream("second", weight: 1)]);
        var selected = new[]
        {
            AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route))).Upstream.Name,
            AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route))).Upstream.Name,
            AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route))).Upstream.Name
        };
        AssertEx.Equal("first,second,first", string.Join(',', selected));
    }

    public static void UpstreamSelectionRouteCopiesUpstreamList()
    {
        using var fixture = SelectorFixture.Create();
        var upstreams = new List<RuntimeUpstream>
        {
            Upstream("first", weight: 1)
        };
        var route = new UpstreamSelectionRoute("route", HealthCheckEnabled: false, upstreams);
        upstreams.Clear();
        var selection = AssertEx.NotNull(fixture.Selector.Select(route));
        AssertEx.Equal("first", selection.Upstream.Name);
        AssertEx.Equal("route", route.Name);
        AssertEx.False(route.HealthCheckEnabled);
        AssertEx.False(route.Upstreams is RuntimeUpstream[], "Upstream selection route should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => new UpstreamSelectionRoute(null!, HealthCheckEnabled: false, []));
        AssertEx.Throws<ArgumentNullException>(() => new UpstreamSelectionRoute("route", HealthCheckEnabled: false, null!));
    }

    public static void ResilienceRuntimeMappersRejectNullInputs()
    {
        var retry = new RuntimeRetryPolicy(true, 2, TimeSpan.FromSeconds(1), true, true, [503], ["GET", "HEAD"], TimeSpan.Zero);
        var route = Route([Upstream("first", weight: 1)], retry: retry);
        var requestHead = RequestHead("GET", Http1RequestFraming.None);
        AssertEx.Throws<ArgumentNullException>(() => ProxyRetryRuntimeMapper.ToAdmissionInput(null!, requestHead));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRetryRuntimeMapper.ToAdmissionInput(route, null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRetryRuntimeMapper.ToOutcomeInput(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyTimeoutRuntimeMapper.ToPolicyInput(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyUpstreamSelectionRuntimeMapper.ToSelectionRoute(null!));
    }

    public static void UnhealthyAndOpenCircuitUpstreamsAreSkipped()
    {
        var first = Upstream("first", weight: 1, circuit: Circuit(threshold: 1));
        var second = Upstream("second", weight: 1);
        using var healthFixture = SelectorFixture.Create();
        var route = Route([first, second], healthEnabled: true);
        healthFixture.Health.RecordHealthCheckResult(HealthTarget(route, first), HealthCheckSample.UnhealthyResult("failed"), healthFixture.Clock.GetUtcNow());
        var firstSelection = AssertEx.NotNull(healthFixture.Selector.Select(SelectionRoute(route)));
        AssertEx.Equal("second", firstSelection.Upstream.Name);
        using var circuitFixture = SelectorFixture.Create();
        var routeWithoutHealth = Route([first, second]);
        var openSelection = AssertEx.NotNull(circuitFixture.Selector.Select(SelectionRoute(routeWithoutHealth)));
        circuitFixture.Circuit.RecordFailure(openSelection.CircuitBreakerLease, "connect_failure");
        var afterOpen = AssertEx.NotNull(circuitFixture.Selector.Select(SelectionRoute(routeWithoutHealth)));
        AssertEx.Equal("second", afterOpen.Upstream.Name);
    }

    public static void MixedProtocolUpstreamFailuresIsolateCircuitState()
    {
        using var fixture = SelectorFixture.Create();
        var http1 = Upstream("http1", weight: 1, circuit: Circuit(threshold: 1));
        var http3 = Upstream("http3", weight: 1, circuit: Circuit(threshold: 1), scheme: "https", protocol: RuntimeUpstreamProtocol.Http3, tls: new RuntimeUpstreamTlsOptions(true, "h3.internal"));
        var route = Route([http1, http3]);
        var first = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(first.CircuitBreakerLease, "connect_failure");
        var second = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        AssertEx.Equal("http3", second.Upstream.Name);
        AssertEx.Equal(CircuitBreakerRuntimeState.Open, fixture.Circuit.Snapshot(StatusSource(http1)).State);
        AssertEx.Equal(CircuitBreakerRuntimeState.Closed, fixture.Circuit.Snapshot(StatusSource(http3)).State);
    }

    public static void AllUpstreamsUnavailableReturnsNoSelection()
    {
        using var fixture = SelectorFixture.Create();
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 1))]);
        var first = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(first.CircuitBreakerLease, "connect_failure");
        AssertEx.True(fixture.Selector.Select(SelectionRoute(route))is null);
        AssertEx.Equal(1L, fixture.Metrics.Snapshot().Resilience.NoAvailableUpstreamFailures);
    }

    public static async Task AllUnavailableUpstreamsReturnSafeFailureAsync()
    {
        var proxyPort = GetFreeTcpPort();
        var closedPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteResilienceSite(temp.Path, proxyPort, [new TestUpstream("first", closedPort, CircuitJson: CircuitJson(threshold: 1, openSeconds: 30))]);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            _ = await SendSingleRequestAsync(proxyPort, "GET /open HTTP/1.1\r\nHost: resilience.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            var second = await SendSingleRequestAsync(proxyPort, "GET /open HTTP/1.1\r\nHost: resilience.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            AssertEx.True(second.Contains("503 Service Unavailable", StringComparison.Ordinal), second);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static void MetricsIncludeRetryCircuitAndBalancingCounters()
    {
        using var fixture = SelectorFixture.Create(includePerUpstreamLabels: true);
        var route = Route([Upstream("first", weight: 1, circuit: Circuit(threshold: 1))]);
        fixture.Metrics.RetryAttempted();
        fixture.Metrics.RetryExhausted();
        var selection = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(selection.CircuitBreakerLease, "connect_failure");
        var text = fixture.Export();
        AssertEx.True(text.Contains("mdrava_retry_attempts_total 1", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("mdrava_retry_exhausted_total 1", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("mdrava_circuit_transitions_total{state=\"open\"} 1", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("mdrava_upstream_selections_total", StringComparison.Ordinal), text);
    }

    public static void EffectiveAndStatusProjectionsShowSafeResilienceState()
    {
        using var fixture = SelectorFixture.Create();
        var upstream = Upstream("first", weight: 2, circuit: Circuit(threshold: 1));
        var route = Route([upstream], retry: new RuntimeRetryPolicy(true, 2, null, true, false, [], ["GET", "HEAD"], TimeSpan.Zero));
        fixture.Store.Replace(fixture.Store.Snapshot.WithListenersAndRoutes(fixture.Store.Snapshot.Listeners, [route]));
        var selection = AssertEx.NotNull(fixture.Selector.Select(SelectionRoute(route)));
        fixture.Circuit.RecordFailure(selection.CircuitBreakerLease, "connect_failure");
        var projection = ProxyConfigurationProjectionMapper.ToProjection(fixture.Store.Snapshot, TestHttp3PlatformSupport.Project(fixture.Store.Snapshot));
        var statusOperations = ProxyStatusOperationFactory.Create(new ProxyRuntimeState(TimeProvider.System), fixture.Metrics, fixture.Store, fixture.Health);
        var statusController = new ProxyStatusController(new ProxyStatusAdministrationService(statusOperations));
        var status = statusController.Get();
        object httpsRedirect = projection.Routes[0].HttpsRedirect;
        AssertEx.True(httpsRedirect is RuntimeHttpsRedirectProjection);
        AssertEx.False(httpsRedirect is RuntimeHttpsRedirectPolicy);
        object canonicalHost = projection.Routes[0].CanonicalHost;
        AssertEx.True(canonicalHost is RuntimeCanonicalHostProjection);
        AssertEx.False(canonicalHost is RuntimeCanonicalHostPolicy);
        object headerPolicy = projection.Routes[0].HeaderPolicy;
        AssertEx.True(headerPolicy is RuntimeHeaderPolicyProjection);
        AssertEx.False(headerPolicy is RuntimeHeaderPolicy);
        object redirect = projection.Routes[0].Redirect;
        AssertEx.True(redirect is RuntimeRedirectProjection);
        AssertEx.False(redirect is RuntimeRedirectPolicy);
        object staticResponse = projection.Routes[0].StaticResponse;
        AssertEx.True(staticResponse is RuntimeStaticResponseProjection);
        AssertEx.False(staticResponse is RuntimeStaticResponse);
        object pathRewrite = projection.Routes[0].PathRewrite;
        AssertEx.True(pathRewrite is RuntimePathRewriteProjection);
        AssertEx.False(pathRewrite is RuntimePathRewritePolicy);
        object maintenance = projection.Routes[0].Maintenance;
        AssertEx.True(maintenance is RuntimeMaintenanceProjection);
        AssertEx.False(maintenance is RuntimeMaintenancePolicy);
        object healthCheck = projection.Routes[0].HealthCheck;
        AssertEx.True(healthCheck is RuntimeHealthCheckProjection);
        AssertEx.False(healthCheck is RuntimeHealthCheckOptions);
        AssertEx.True(projection.Routes[0].Retry.Enabled);
        object retry = projection.Routes[0].Retry;
        object retryStatusCodes = projection.Routes[0].Retry.RetryOnStatusCodes;
        object retryMethods = projection.Routes[0].Retry.RetryMethods;
        AssertEx.True(retry is RuntimeRetryProjection);
        AssertEx.False(retry is RuntimeRetryPolicy);
        AssertEx.False(retryStatusCodes is int[]);
        AssertEx.False(retryMethods is string[]);
        object cache = projection.Routes[0].Cache;
        object cacheVaryHeaders = projection.Routes[0].Cache.VaryByHeaders;
        object cacheStatusCodes = projection.Routes[0].Cache.CacheableStatusCodes;
        object cacheMethods = projection.Routes[0].Cache.Methods;
        AssertEx.True(cache is RuntimeCacheProjection);
        AssertEx.False(cache is RuntimeCachePolicy);
        AssertEx.False(cacheVaryHeaders is string[]);
        AssertEx.False(cacheStatusCodes is int[]);
        AssertEx.False(cacheMethods is string[]);
        object resolvedOptions = projection.Routes[0].ResolvedOptions;
        AssertEx.True(resolvedOptions is RuntimeRouteResolvedOptionsProjection);
        AssertEx.False(resolvedOptions is RuntimeRouteResolvedOptions);
        AssertEx.Equal(2, projection.Routes[0].Upstreams[0].Weight);
        AssertEx.Equal(true, projection.Routes[0].Upstreams[0].CircuitBreaker.Enabled);
        object circuitBreaker = projection.Routes[0].Upstreams[0].CircuitBreaker;
        object failureStatusCodes = projection.Routes[0].Upstreams[0].CircuitBreaker.FailureStatusCodes;
        AssertEx.True(circuitBreaker is RuntimeCircuitBreakerProjection);
        AssertEx.False(circuitBreaker is RuntimeCircuitBreakerPolicy);
        AssertEx.False(failureStatusCodes is int[]);
        AssertEx.Equal(CircuitBreakerRuntimeStateResponse.Open, status.Upstreams[0].CircuitBreaker.State);
        AssertEx.Equal(2, status.Upstreams[0].Weight);
    }

    public static void RetryPolicySuppressesConfiguredStatusOnlyWhenAllowed()
    {
        var retry = new ProxyRetryOutcomeInput(true, false, false, [503]);
        AssertEx.True(ProxyRetryPolicy.ShouldSuppressRetryableStatusResponse(retry, 503, true));
        AssertEx.False(ProxyRetryPolicy.ShouldSuppressRetryableStatusResponse(retry, 503, false));
        AssertEx.False(ProxyRetryPolicy.ShouldSuppressRetryableStatusResponse(retry, 502, true));
        AssertEx.False(ProxyRetryPolicy.ShouldSuppressRetryableStatusResponse(new ProxyRetryOutcomeInput(false, retry.RetryOnConnectFailure, retry.RetryOnUpstreamResponseHeadTimeout, retry.RetryOnStatusCodes), 503, true));
    }

    public static void RetryPolicySuppressesAttemptFailureOnlyBeforeFinalAttempt()
    {
        AssertEx.True(ProxyRetryPolicy.ShouldSuppressAttemptFailureResponse(retryAllowed: true, attempt: 1, maxAttempts: 2));
        AssertEx.False(ProxyRetryPolicy.ShouldSuppressAttemptFailureResponse(retryAllowed: true, attempt: 2, maxAttempts: 2));
        AssertEx.False(ProxyRetryPolicy.ShouldSuppressAttemptFailureResponse(retryAllowed: false, attempt: 1, maxAttempts: 2));
    }

    public static void RetryPolicyNamesAdmissionDecisions()
    {
        var disabled = ProxyRetryPolicy.EvaluateAdmission(RetryAdmissionInput(enabled: false, maxAttempts: 2, retryMethods: ["GET", "HEAD"], requestMethod: "GET", hasRequestBody: false));
        var methodSkipped = ProxyRetryPolicy.EvaluateAdmission(RetryAdmissionInput(enabled: true, maxAttempts: 2, retryMethods: ["GET", "HEAD"], requestMethod: "POST", hasRequestBody: false));
        var bodySkipped = ProxyRetryPolicy.EvaluateAdmission(RetryAdmissionInput(enabled: true, maxAttempts: 2, retryMethods: ["GET", "HEAD"], requestMethod: "GET", hasRequestBody: true));
        var allowed = ProxyRetryPolicy.EvaluateAdmission(RetryAdmissionInput(enabled: true, maxAttempts: 2, retryMethods: ["GET", "HEAD"], requestMethod: "GET", hasRequestBody: false));
        AssertEx.Equal(ProxyRetryAdmissionDecision.NotAllowed, disabled);
        AssertEx.True(methodSkipped is ProxyRetryAdmissionDecision.SkippedDecision);
        AssertEx.Equal("method", ((ProxyRetryAdmissionDecision.SkippedDecision)methodSkipped).Reason);
        AssertEx.True(bodySkipped is ProxyRetryAdmissionDecision.SkippedDecision);
        AssertEx.Equal("request_body", ((ProxyRetryAdmissionDecision.SkippedDecision)bodySkipped).Reason);
        AssertEx.Equal(ProxyRetryAdmissionDecision.Allowed, allowed);
        AssertEx.Throws<ArgumentNullException>(() => RetryAdmissionInput(enabled: true, maxAttempts: 2, retryMethods: null!, requestMethod: "GET", hasRequestBody: false));
        AssertEx.Throws<ArgumentNullException>(() => RetryAdmissionInput(enabled: true, maxAttempts: 2, retryMethods: [null!], requestMethod: "GET", hasRequestBody: false));
        AssertEx.Throws<ArgumentNullException>(() => RetryAdmissionInput(enabled: true, maxAttempts: 2, retryMethods: ["GET"], requestMethod: null!, hasRequestBody: false));
    }

    public static void RetryPolicyCreatesExecutionPlanFromAdmission()
    {
        var disabled = ProxyRetryPolicy.CreatePlan(RetryAdmissionInput(enabled: false, maxAttempts: 3, retryMethods: ["GET"], requestMethod: "GET", hasRequestBody: false));
        var skipped = ProxyRetryPolicy.CreatePlan(RetryAdmissionInput(enabled: true, maxAttempts: 3, retryMethods: ["GET"], requestMethod: "POST", hasRequestBody: false));
        var allowed = ProxyRetryPolicy.CreatePlan(RetryAdmissionInput(enabled: true, maxAttempts: 3, retryMethods: ["GET"], requestMethod: "GET", hasRequestBody: false));
        AssertEx.Equal(ProxyRetryAdmissionDecision.NotAllowed, disabled.Admission);
        AssertEx.False(disabled.IsAllowed);
        AssertEx.Equal(1, disabled.MaxAttempts);
        AssertEx.True(skipped.Admission is ProxyRetryAdmissionDecision.SkippedDecision);
        AssertEx.False(skipped.IsAllowed);
        AssertEx.Equal(1, skipped.MaxAttempts);
        AssertEx.Equal(ProxyRetryAdmissionDecision.Allowed, allowed.Admission);
        AssertEx.True(allowed.IsAllowed);
        AssertEx.Equal(3, allowed.MaxAttempts);
        var malformedAllowed = ProxyRetryPolicy.CreatePlan(RetryAdmissionInput(enabled: true, maxAttempts: 0, retryMethods: ["GET"], requestMethod: "GET", hasRequestBody: false));
        AssertEx.True(malformedAllowed.IsAllowed);
        AssertEx.Equal(1, malformedAllowed.MaxAttempts);
    }

    public static void RetryPolicyNamesAttemptDecisions()
    {
        var retry = new ProxyRetryOutcomeInput(true, true, false, []);
        var retryableFailure = ForwardingResult.Failure(responseStarted: false, responseStatusCode: null, failureKind: ProxyFailureKind.UpstreamConnectFailed);
        var startedFailure = ForwardingResult.Failure(responseStarted: true, responseStatusCode: null, failureKind: ProxyFailureKind.UpstreamConnectFailed);
        var success = ForwardingResult.Success(responseStarted: true, keepClientConnectionOpen: true, responseStatusCode: 200);
        var firstAttempt = ProxyRetryPolicy.EvaluateAttempt(retry, retryableFailure, attempt: 1, maxAttempts: 2);
        var finalAttempt = ProxyRetryPolicy.EvaluateAttempt(retry, retryableFailure, attempt: 2, maxAttempts: 2);
        var responseStarted = ProxyRetryPolicy.EvaluateAttempt(retry, startedFailure, attempt: 1, maxAttempts: 2);
        var nonRetryable = ProxyRetryPolicy.EvaluateAttempt(retry, success, attempt: 1, maxAttempts: 2);
        AssertEx.Equal(ProxyRetryAttemptDecision.Retry, firstAttempt);
        AssertEx.Equal(ProxyRetryAttemptDecision.Stop, finalAttempt);
        AssertEx.True(responseStarted is ProxyRetryAttemptDecision.SkippedDecision);
        AssertEx.Equal("response_started", ((ProxyRetryAttemptDecision.SkippedDecision)responseStarted).Reason);
        AssertEx.Equal(ProxyRetryAttemptDecision.Stop, nonRetryable);
        AssertEx.False(ProxyRetryPolicy.DidExhaustAttempts(retry, retryableFailure, attempt: 1, maxAttempts: 2));
        AssertEx.True(ProxyRetryPolicy.DidExhaustAttempts(retry, retryableFailure, attempt: 2, maxAttempts: 2));
        AssertEx.True(ProxyRetryPolicy.DidExhaustAttempts(retry, startedFailure, attempt: 2, maxAttempts: 2));
        AssertEx.False(ProxyRetryPolicy.DidExhaustAttempts(retry, success, attempt: 2, maxAttempts: 2));
        AssertEx.False(ProxyRetryPolicy.DidExhaustAttemptsBeforeUpstreamSelection(attempt: 1));
        AssertEx.True(ProxyRetryPolicy.DidExhaustAttemptsBeforeUpstreamSelection(attempt: 2));
        AssertEx.Equal(success, ProxyRetryPolicy.RequireCompletedAttemptResult(success));
    }

    public static void ForwardingResultNamesSuccessAndFailureOutcomes()
    {
        var success = ForwardingResult.Success(responseStarted: true, keepClientConnectionOpen: true, responseStatusCode: 200);
        var failure = ForwardingResult.Failure(responseStarted: false, responseStatusCode: 502, failureKind: ProxyFailureKind.UpstreamConnectFailed);
        var tunnelRelay = TunnelRelayResult.RelayFailed(10, 8, TimeSpan.FromSeconds(1));
        var tunnel = ForwardingResult.TunnelCompleted(responseStatusCode: 101, tunnel: tunnelRelay);
        AssertEx.True(success is ForwardingResult.SuccessResult);
        AssertEx.True(success.ResponseStarted);
        AssertEx.True(success.KeepClientConnectionOpen);
        AssertEx.Equal(200, success.ResponseStatusCode!.Value);
        AssertEx.Equal(ProxyFailureKind.None, success.FailureKind);
        AssertEx.True(failure is ForwardingResult.FailureResult);
        AssertEx.False(failure.ResponseStarted);
        AssertEx.False(failure.KeepClientConnectionOpen);
        AssertEx.Equal(502, failure.ResponseStatusCode!.Value);
        AssertEx.Equal(ProxyFailureKind.UpstreamConnectFailed, failure.FailureKind);
        AssertEx.True(tunnel is ForwardingResult.TunnelCompletedResult);
        AssertEx.True(tunnel.ResponseStarted);
        AssertEx.False(tunnel.KeepClientConnectionOpen);
        AssertEx.Equal(101, tunnel.ResponseStatusCode!.Value);
        AssertEx.Equal(ProxyFailureKind.TunnelRelayFailure, tunnel.FailureKind);
        AssertEx.Equal(tunnelRelay, ((ForwardingResult.TunnelCompletedResult)tunnel).Tunnel);
    }

    public static void TimeoutPolicyAppliesRouteAndRetryAttemptTimeouts()
    {
        var baseTimeouts = Timeouts();
        var input = new ProxyRouteTimeoutPolicyInput(UpstreamResponseHeadTimeout: TimeSpan.FromSeconds(30), RetryPerAttemptTimeout: TimeSpan.FromSeconds(2));
        var routeTimeouts = ProxyTimeoutPolicy.ApplyRouteTimeouts(input, baseTimeouts);
        var retryAttemptTimeouts = ProxyTimeoutPolicy.ApplyRetryAttemptTimeout(input, routeTimeouts);
        AssertEx.Equal(TimeSpan.FromSeconds(10), baseTimeouts.UpstreamConnectTimeout);
        AssertEx.Equal(TimeSpan.FromSeconds(10), baseTimeouts.UpstreamResponseHeadTimeout);
        AssertEx.Equal(TimeSpan.FromSeconds(10), routeTimeouts.UpstreamConnectTimeout);
        AssertEx.Equal(TimeSpan.FromSeconds(30), routeTimeouts.UpstreamResponseHeadTimeout);
        AssertEx.Equal(TimeSpan.FromSeconds(2), retryAttemptTimeouts.UpstreamConnectTimeout);
        AssertEx.Equal(TimeSpan.FromSeconds(2), retryAttemptTimeouts.UpstreamResponseHeadTimeout);
        AssertEx.Equal(routeTimeouts.DownstreamWriteTimeout, retryAttemptTimeouts.DownstreamWriteTimeout);
    }

    private static Http1RequestHead RequestHead(string method, Http1RequestFraming framing)
    {
        return new Http1RequestHead(method, "/", "/", "HTTP/1.1", "resilience.test", framing, []);
    }

    private static ProxyRetryAdmissionInput RetryAdmissionInput(bool enabled, int maxAttempts, IReadOnlyList<string> retryMethods, string requestMethod, bool hasRequestBody)
    {
        return new ProxyRetryAdmissionInput(enabled, maxAttempts, retryMethods, requestMethod, hasRequestBody);
    }

    private static async Task<ClosedUpstreamResult> RunClosedUpstreamScenarioAsync(string request, string retryJson)
    {
        var proxyPort = GetFreeTcpPort();
        var closedPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteResilienceSite(temp.Path, proxyPort, [new TestUpstream("first", closedPort)], retryJson);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, request, timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            return new ClosedUpstreamResult(response, metrics);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<string>> RunCountingUpstreamAsync(int upstreamPort, int expectedRequests, Func<int, string> responseFactory, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        List<string> requests = [];
        try
        {
            while (requests.Count < expectedRequests)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                var request = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                requests.Add(request);
                var response = Encoding.ASCII.GetBytes(responseFactory(requests.Count));
                await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            }

            return requests;
        }
        finally
        {
            listener.Stop();
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

    private static async Task<string> ReadRequestHeadAsync(Stream stream, CancellationToken cancellationToken)
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
            if (total >= 4 && buffer[total - 4] == '\r' && buffer[total - 3] == '\n' && buffer[total - 2] == '\r' && buffer[total - 1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static void WriteResilienceSite(string dataDirectory, int proxyPort, IReadOnlyList<TestUpstream> upstreams, string retryJson = "")
    {
        var upstreamJson = string.Join(",", upstreams.Select(upstream => $$"""
                {
                  "name": "{{upstream.Name}}",
                  "address": "127.0.0.1",
                  "port": {{upstream.Port}},
                  "weight": {{upstream.Weight}}
                  {{upstream.CircuitJson}}
                }
            """));
        ConfigurationTests.WriteCustomSite(dataDirectory, "resilience.json", $$"""
            {
              "name": "resilience",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}}
                }
              ],
              "host": "*",
              "pathPrefix": "/",
              {{retryJson}}
              "upstreams": [
                {{upstreamJson}}
              ]
            }
            """);
    }

    private static string RetryJson(int maxAttempts, bool retryOnConnectFailure)
    {
        return $$"""
              "retry": {
                "enabled": true,
                "maxAttempts": {{maxAttempts}},
                "retryOnConnectFailure": {{retryOnConnectFailure.ToString().ToLowerInvariant()}},
                "retryMethods": ["GET", "HEAD"],
                "retryBackoffMilliseconds": 0
              },
        """;
    }

    private static string RetryStatusJson(int maxAttempts, int statusCode)
    {
        return $$"""
              "retry": {
                "enabled": true,
                "maxAttempts": {{maxAttempts}},
                "retryOnStatusCodes": [{{statusCode}}],
                "retryMethods": ["GET", "HEAD"],
                "retryBackoffMilliseconds": 0
              },
        """;
    }

    private static string CircuitJson(int threshold, int openSeconds)
    {
        return $$"""
                  ,
                  "circuitBreaker": {
                    "enabled": true,
                    "failureThreshold": {{threshold}},
                    "samplingWindowSeconds": 60,
                    "openDurationSeconds": {{openSeconds}},
                    "halfOpenMaxAttempts": 1
                  }
        """;
    }

    private static IHost BuildProxyHost(string dataDirectory)
    {
        return Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Mdrava:DataDirectory"] = dataDirectory });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) => services.AddProxyDataPlane(context.Configuration)).Build();
    }

    private static RuntimeRoute Route(IReadOnlyList<RuntimeUpstream> upstreams, bool healthEnabled = false, RuntimeRetryPolicy? retry = null)
    {
        return new RuntimeRoute("route", "*", "/", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(healthEnabled, "/health", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1, 1), upstreams, new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", ""), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), RuntimeCachePolicy.Disabled, new RuntimeRouteResolvedOptions(104857600, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true), SiteName: "site", Retry: retry ?? RuntimeRetryPolicy.Disabled);
    }

    private static UpstreamSelectionRoute SelectionRoute(RuntimeRoute route)
    {
        return new UpstreamSelectionRoute(route.Name, route.HealthCheck.Enabled, route.Upstreams);
    }

    private static RuntimeUpstream Upstream(string name, int weight, RuntimeCircuitBreakerPolicy? circuit = null, string scheme = "http", string protocol = RuntimeUpstreamProtocol.Http1, RuntimeUpstreamTlsOptions? tls = null)
    {
        return new RuntimeUpstream("route", name, scheme, protocol, "127.0.0.1", string.Equals(name, "first", StringComparison.Ordinal) ? 15000 : 15001, weight, tls ?? new RuntimeUpstreamTlsOptions(true, null), circuit ?? RuntimeCircuitBreakerPolicy.Disabled);
    }

    private static RuntimeCircuitBreakerPolicy Circuit(int threshold, int openSeconds = 30, IReadOnlyList<int>? failureStatusCodes = null)
    {
        return new RuntimeCircuitBreakerPolicy(true, threshold, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(openSeconds), 1, failureStatusCodes ?? []);
    }

    private static ProxyConfigurationStore CreateStore(RuntimeMetricsOptions? metricsOptions = null, IReadOnlyList<RuntimeRoute>? routes = null)
    {
        var store = new ProxyConfigurationStore();
        store.Replace(new ProxyConfigurationSnapshot(1, DateTimeOffset.UtcNow, "tests", [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []), new RuntimeAdminSecurityOptions([], false, false, null, "MDRAVA_ADMIN_TOKEN", "none", 100), new RuntimeAcmeOptions(false, true, "", [], false, "acme", 30, 720, 60, []), Timeouts(), new RuntimeConnectionLimits(100, 16, 1024), new RuntimeObservabilityOptions(true, 100, new RuntimeLogPersistenceOptions(true, true, 1_048_576, 8)), new RuntimeLimits(4096, 128, 240, 30, 32768, 128, 8192, 104857600, 8192, TimeSpan.FromSeconds(15)), new RuntimeForwardedHeadersOptions(true, []), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), [], routes ?? [], metricsOptions ?? RuntimeMetricsOptions.Default));
        return store;
    }

    private static RuntimeTimeouts Timeouts()
    {
        return new RuntimeTimeouts(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
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

    private sealed record TestUpstream(string Name, int Port, int Weight = 1, string CircuitJson = "");
    private sealed record ClosedUpstreamResult(string Response, ProxyMetricsSnapshot Metrics);
    private static CircuitBreakerStatusSource StatusSource(RuntimeUpstream upstream)
    {
        return CircuitBreakerStatusSourceMapper.FromUpstream(upstream);
    }

    private static UpstreamHealthCheckTarget HealthTarget(RuntimeRoute route, RuntimeUpstream upstream)
    {
        return new UpstreamHealthCheckTarget(route.Name, upstream.Name, upstream.Endpoint, upstream.Identity, UpstreamTransportEndpointMapper.FromUpstream(upstream), route.HealthCheck.Path, route.HealthCheck.Interval, route.HealthCheck.Timeout, route.HealthCheck.HealthyThreshold, route.HealthCheck.UnhealthyThreshold);
    }

    private sealed class SelectorFixture : IDisposable
    {
        private readonly UpstreamConnectionPool _pool;
        private SelectorFixture(ProxyMetrics metrics, ManualTimeProvider clock, CircuitBreakerStore circuit, UpstreamHealthStore health, RoundRobinUpstreamSelector selector, ResponseCacheStore cache, AcmeCertificateStatusStore acme, PrometheusMetricsExporter exporter, ProxyConfigurationStore store, UpstreamConnectionPool pool)
        {
            Metrics = metrics;
            Clock = clock;
            Circuit = circuit;
            Health = health;
            Selector = selector;
            Cache = cache;
            Acme = acme;
            Exporter = exporter;
            Store = store;
            _pool = pool;
        }

        public ProxyMetrics Metrics { get; }
        public ManualTimeProvider Clock { get; }
        public CircuitBreakerStore Circuit { get; }
        public UpstreamHealthStore Health { get; }
        public RoundRobinUpstreamSelector Selector { get; }
        public ResponseCacheStore Cache { get; }
        public AcmeCertificateStatusStore Acme { get; }
        public PrometheusMetricsExporter Exporter { get; }
        public ProxyConfigurationStore Store { get; }

        public string Export()
        {
            return Exporter.Export(MetricsExportInput(Store.Snapshot, Metrics.Snapshot(), Cache.ReadStatusSnapshot(), Health.ReadUpstreams(ProxyUpstreamHealthSourceMapper.FromRoutes(Store.Snapshot.Routes)), Acme.Snapshot()));
        }

        private static ProxyMetricsExportInput MetricsExportInput(ProxyConfigurationSnapshot snapshot, ProxyMetricsSnapshot metrics, ProxyCacheRuntimeStatusSnapshot cacheRuntime, IReadOnlyList<ProxyUpstreamStatus> upstreamHealth, IReadOnlyList<AcmeCertificateLifecycleStatus> acmeCertificates)
        {
            return ProxyMetricsExportInputMapper.FromSources(metrics, ProxyMetricsExportLabelOptionsMapper.FromMetrics(snapshot.Metrics), ProxyMetricsExportHttp3FactsMapper.FromSources(snapshot.Listeners, snapshot.Routes), ProxyCacheStatusReader.Project(ProxyCacheStatusRouteSourceMapper.ToRouteSources(snapshot.Routes), cacheRuntime), upstreamHealth, acmeCertificates);
        }

        public static SelectorFixture Create(bool includePerUpstreamLabels = false)
        {
            var metrics = new ProxyMetrics();
            var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            var circuit = new CircuitBreakerStore(metrics, clock);
            var pool = new UpstreamConnectionPool(new UpstreamConnectionFactory(), metrics, clock);
            var health = new UpstreamHealthStore(metrics, pool, circuit);
            var selector = new RoundRobinUpstreamSelector(health, circuit, metrics);
            var cache = new ResponseCacheStore(clock);
            var store = CreateStore(new RuntimeMetricsOptions(RuntimeMetricsOptions.Default.Enabled, RuntimeMetricsOptions.Default.EndpointPath, RuntimeMetricsOptions.Default.ProtectedByAdminAuth, RuntimeMetricsOptions.Default.IncludePerRouteLabels, includePerUpstreamLabels, RuntimeMetricsOptions.Default.PublicMetricsEnabled), [Route([Upstream("first", 1, Circuit(1))])]);
            var acme = new AcmeCertificateStatusStore();
            var exporter = new PrometheusMetricsExporter();
            return new SelectorFixture(metrics, clock, circuit, health, selector, cache, acme, exporter, store, pool);
        }

        public void Dispose()
        {
            _pool.Dispose();
        }
    }

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

        public void Advance(TimeSpan value)
        {
            _utcNow = _utcNow.Add(value);
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-resilience-tests-{Guid.NewGuid():N}");
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
