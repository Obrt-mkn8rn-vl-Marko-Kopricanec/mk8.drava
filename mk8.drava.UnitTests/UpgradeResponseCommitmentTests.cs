using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class UpgradeResponseCommitmentTests
{
    [Theory]
    [InlineData("fixed", true)]
    [InlineData("chunked", true)]
    [InlineData("accepted-head", true)]
    [InlineData("rejected-head", true)]
    [InlineData("invalid-head", false)]
    [InlineData("no-head", false)]
    public async Task UpgradeFailureCannotGenerateAnotherResponseAfterTheFirstWriteAttemptAsync(string kind, bool committed)
    {
        ArgumentNullException.ThrowIfNull(kind);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var destination = new DevelopmentPartialHeadStream(kind.EndsWith("-head", StringComparison.Ordinal) && committed);
        await using var destinationLifetime = destination.ConfigureAwait(true);
        var result = await ForwardAndJoinPeerAsync(listener, destination, port, Response(kind), deadline).ConfigureAwait(true);
        Assert.IsType<ForwardingResult.FailureResult>(result);
        Assert.Equal(committed, result.ResponseStarted);
        Assert.False(result.KeepClientConnectionOpen);
        var observed = Encoding.ASCII.GetString(destination.ToArray());
        Assert.Equal(1, observed.Split("HTTP/1.1 ", StringSplitOptions.None).Length - 1);
        if (committed)
        {
            Assert.Null(result.ResponseStatusCode);
            Assert.DoesNotContain("502 Bad Gateway", observed, StringComparison.Ordinal);
            Assert.StartsWith(string.Equals(kind, "accepted-head", StringComparison.Ordinal) ? "HTTP/1.1 101" : "HTTP/1.1 403", observed, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(502, result.ResponseStatusCode);
            Assert.StartsWith("HTTP/1.1 502 Bad Gateway", observed, StringComparison.Ordinal);
        }
    }

    private static async Task<ForwardingResult> ForwardAndJoinPeerAsync(TcpListener listener, Stream destination, int port,
        string response, CancellationTokenSource deadline)
    {
        var peer = ReplyAsync(listener, response, deadline.Token);
        try
        {
            var result = await ForwardAsync(destination, port, deadline.Token).ConfigureAwait(false);
            await peer.ConfigureAwait(false);
            return result;
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            listener.Stop();
            try { await peer.ConfigureAwait(false); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
    }

    private static async Task<ForwardingResult> ForwardAsync(Stream destination, int port, CancellationToken cancellationToken)
    {
        var metrics = new ProxyMetrics();
        var relay = new TunnelRelay(metrics, NullLogger<TunnelRelay>.Instance, TimeProvider.System);
        var forwarder = new UpgradeForwarder(new UpstreamConnectionFactory(), new HopByHopHeaderPolicy(), relay, metrics, NullLogger<UpgradeForwarder>.Instance);
        var route = ProxyConfigurationRuntimeMapper.ToRuntimeRoutes([new ProxyRouteOptions { Name = "fixture", Host = "service.test",
            Upstreams = [new UpstreamOptions { Name = "peer", Address = IPAddress.Loopback.ToString(), Port = port }], }], new ProxyOperationalOptions())[0];
        var listener = ProxyConfigurationRuntimeMapper.ToRuntimeListeners([new ListenerOptions { Name = "fixture" }])[0];
        var request = new Http1RequestHead("GET", "/probe", "/probe", "HTTP/1.1", "service.test", Http1RequestFraming.None,
            [new("Host", "service.test"), new("Connection", "Upgrade"), new("Upgrade", "mk8-echo")]);
        return await forwarder.ForwardAsync(destination, request, new UpgradeRequestInfo("mk8-echo", IsWebSocket: false, WebSocketKey: null), route, route.Upstreams[0],
            listener, RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(2)), new RuntimeConnectionLimits(10, 1, 1), "/probe",
            new ForwardedHeadersContext(ResolvedClientAddress: null, ResolvedClientEndpoint: null, Headers: []), "fixture", cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplyAsync(TcpListener listener, string response, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();
        var one = new byte[1];
        uint ending = 0;
        for (var count = 0; count < 32768; count++)
        {
            await stream.ReadExactlyAsync(one, cancellationToken).ConfigureAwait(false);
            ending = (ending << 8) | one[0];
            if (ending != 0x0d0a0d0a) continue;
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        throw new InvalidDataException("Controlled peer received an oversized request head.");
    }

    private static string Response(string kind) => kind switch
    {
        "fixed" => "HTTP/1.1 403 Forbidden\r\nContent-Length: 2\r\n\r\nx",
        "chunked" => "HTTP/1.1 403 Forbidden\r\nTransfer-Encoding: chunked\r\n\r\n2\r\nx",
        "accepted-head" => "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: mk8-echo\r\n\r\n",
        "rejected-head" => "HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n",
        "invalid-head" => "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: wrong\r\n\r\n",
        "no-head" => "",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
