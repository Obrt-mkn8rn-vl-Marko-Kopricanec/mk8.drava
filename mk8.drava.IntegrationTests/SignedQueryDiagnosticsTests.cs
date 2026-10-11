using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Observability;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Status;
using Mk8.Drava.Application.INF.Observability;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class SignedQueryDiagnosticsTests(ITestOutputHelper output)
{
    private const string SecretFixture = "nonsecret-fixture-signature";
    private const string RawTarget = "/objects/a%2Fb?sig=" + SecretFixture + "&marker=%2F%2B%3D";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AccessDiagnosticsNeverPersistTheQueryWhileTheRequestKeepsItsRawTarget(bool accessEnabled)
    {
        var metrics = new ProxyMetrics();
        var recent = new RecentRequestDiagnosticsStore(metrics);
        var persistence = new AccessCapture();
        var context = new ProxyRequestContext("fixture", "public", "http", clientEndpoint: null, configVersion: 1, TimeProvider.System);
        context.SetRequest("GET", "app.test", RawTarget, externalRequestId: null);
        new AccessLogEmitter(recent, metrics, NullLogger<AccessLogEmitter>.Instance, persistence).Complete(context, accessEnabled, diagnosticsCapacity: 10);
        Assert.Equal(RawTarget, context.Target);
#pragma warning disable HLQ005 // Single verifies required cardinality; First would weaken the privacy assertion.
        Assert.Equal("/objects/a%2Fb", Assert.Single(recent.Recent(10)).Target);
        if (accessEnabled) Assert.Equal("/objects/a%2Fb", Assert.Single(persistence.Entries).Target);
#pragma warning restore HLQ005
        else Assert.Empty(persistence.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualForwardingLogsExcludeSignedQueriesWithoutChangingTheOriginRequestAsync(bool malformedOrigin)
    {
        using var origin = new DevelopmentHttp1Peer();
        var proxy = await TwoProcessProxy.StartAsync(origin.Port, observeForwarding: true).ConfigureAwait(true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var rawResponse = malformedOrigin ? "HTTP/1.1 200 OK\r\nContent-Length: invalid\r\n\r\n" : "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok";
        var exchange = origin.RespondOnceAndCaptureHeadAsync(rawResponse, deadline.Token);
        string actualHead;
        await using (proxy.ConfigureAwait(true))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(RawTarget, UriKind.Relative));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "public-fixture-authorization");
                request.Headers.Add("x-ms-version", "2026-06-06");
                using var response = await proxy.Client.SendAsync(request, deadline.Token).ConfigureAwait(true);
                Assert.Equal(malformedOrigin ? HttpStatusCode.BadGateway : HttpStatusCode.OK, response.StatusCode);
                actualHead = await exchange.ConfigureAwait(true);
            }
            finally
            {
                await deadline.CancelAsync().ConfigureAwait(true);
                try { await exchange.ConfigureAwait(true); }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            }
        }
        Assert.StartsWith("GET " + RawTarget + " HTTP/1.1\r\n", actualHead, StringComparison.Ordinal);
        Assert.Contains("Host: app.test\r\n", actualHead, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorization: Bearer public-fixture-authorization\r\n", actualHead, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("x-ms-version: 2026-06-06\r\n", actualHead, StringComparison.OrdinalIgnoreCase);
        output.WriteLine("fixture={0}; origin_head_verified=true; malformed_origin={1}", proxy.EvidenceId, malformedOrigin);
        Assert.Contains(malformedOrigin ? "[10016]" : "[10013]", proxy.ApplicationLog, StringComparison.Ordinal);
        Assert.Contains("/objects/a%2Fb", proxy.ApplicationLog, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretFixture, proxy.ApplicationLog, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretFixture, proxy.GatewayLog, StringComparison.Ordinal);
    }

    private sealed class AccessCapture : IProxyLogPersistenceStore
    {
        internal List<ProxyAccessLogEntry> Entries { get; } = [];
        public void WriteAccess(ProxyAccessLogEntry entry) => Entries.Add(entry);
        public void WriteAdminAudit(Mk8.Drava.Application.BLL.ControlPlane.AdminAudit.ProxyAdminAuditEvent auditEvent) { }
        public ProxyLogPersistenceStatus GetStatus() => ProxyLogPersistenceStatus.Unknown;
    }
}
