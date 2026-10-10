using System.Collections.Concurrent;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Authentication;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Mk8.Drava.UnitTests;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class VerifiedUpstreamQuicTrustTests
{
    [Theory]
    [InlineData("matching")]
    [InlineData("wrong-name")]
    [InlineData("system-trust")]
    public async Task ActualQuicRequiresTheConfiguredRootAndNameBeforeRequestFramesAsync(string profile)
    {
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var rootPath = Path.Combine(directory.Path, "upstream-root.cer");
        await File.WriteAllBytesAsync(rootPath, certificates.Root.RawData).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(rootPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var names = new ConcurrentQueue<string?>();
        var listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0), ApplicationProtocols = [new SslApplicationProtocol("h3")],
            ConnectionOptionsCallback = (_, hello, _) =>
            {
                names.Enqueue(hello.ServerName);
                return ValueTask.FromResult(new QuicServerConnectionOptions
                {
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions { ServerCertificate = certificates.Leaf, ApplicationProtocols = [new SslApplicationProtocol("h3")] },
                    MaxInboundUnidirectionalStreams = 4, MaxInboundBidirectionalStreams = 4, DefaultCloseErrorCode = 0x100, DefaultStreamErrorCode = 0x100,
                });
            },
        }, deadline.Token).ConfigureAwait(true);
        await using var listenerLifetime = listener.ConfigureAwait(true);
        var endpoint = new UpstreamTransportEndpoint("peer", "https", "http3", "127.0.0.1", listener.LocalEndPoint.Port, true,
            string.Equals(profile, "wrong-name", StringComparison.Ordinal) ? "other.drava.invalid" : "backend.drava.invalid")
        {
            TrustedRoot = string.Equals(profile, "system-trust", StringComparison.Ordinal) ? null
                : new RuntimeTrustedRootCertificate(rootPath, certificates.Root.GetCertHashString(HashAlgorithmName.SHA256)),
        };
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var metrics = new ProxyMetrics();
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var serving = ServeAsync(listener, finished.Task, deadline.Token);
        try
        {
            if (string.Equals(profile, "matching", StringComparison.Ordinal))
                await ExchangeAsync(endpoint, timeouts, metrics, deadline.Token).ConfigureAwait(true);
            else
                await Assert.ThrowsAsync<Http3UpstreamProtocolException>(async () =>
                {
                    var unexpected = await Http3UpstreamConnection.ConnectAsync(endpoint, timeouts, metrics, 16384, deadline.Token).ConfigureAwait(true);
                    await using var unexpectedLifetime = unexpected.ConfigureAwait(true);
                }).ConfigureAwait(true);
            Assert.Contains(endpoint.EffectiveSniHost, names, StringComparer.Ordinal);
            Assert.Equal(0, metrics.Snapshot().UpstreamHttp3.ActiveConnections);
        }
        finally
        {
            finished.TrySetResult();
            await deadline.CancelAsync().ConfigureAwait(true);
            await listener.DisposeAsync().ConfigureAwait(true);
            try { await serving.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or QuicException or ObjectDisposedException) { }
            catch (AuthenticationException) when (!string.Equals(profile, "matching", StringComparison.Ordinal)) { }
        }
    }

    private static async Task ExchangeAsync(UpstreamTransportEndpoint endpoint, RuntimeTimeouts timeouts, ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        var connection = await Http3UpstreamConnection.ConnectAsync(endpoint, timeouts, metrics, 16384, cancellationToken).ConfigureAwait(true);
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await connection.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "svc.site.test"), new(":path", "/verified")], true, timeouts, cancellationToken).ConfigureAwait(true);
        Assert.Equal(200, (await connection.ReadResponseHeadAsync(4096, timeouts, cancellationToken).ConfigureAwait(true)).StatusCode);
        Assert.Equal("ok"u8.ToArray(), (await connection.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(true)).Data);
        Assert.True((await connection.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(true)).EndStream);
        var first = connection.DisposeAsync().AsTask();
        var second = connection.DisposeAsync().AsTask();
        await Task.WhenAll(first, second).WaitAsync(cancellationToken).ConfigureAwait(true);
        Assert.Equal(1, metrics.Snapshot().UpstreamHttp3.PoolConnectionsClosed);
    }

    private static async Task ServeAsync(QuicListener listener, Task finished, CancellationToken cancellationToken)
    {
        var peer = await listener.AcceptConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var peerLifetime = peer.ConfigureAwait(false);
        List<QuicStream> controls = [];
        try
        {
            while (true)
            {
                var stream = await peer.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Type != QuicStreamType.Bidirectional) { controls.Add(stream); continue; }
                await using var streamLifetime = stream.ConfigureAwait(false);
                await RespondAsync(stream, cancellationToken).ConfigureAwait(false);
                await finished.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        finally
        {
            for (var index = 0; index < controls.Count; index++) await controls[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task RespondAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        var offset = 0;
        Assert.True(Http3Codec.TryReadFrame(request.ToArray(), ref offset, out var type, out var payload));
        Assert.Equal(Http3Codec.HeadersFrame, type);
        Assert.True(Http3Codec.TryDecodeHeaderBlock(payload.Span, 4096, out var fields, out var reason), reason);
        Assert.Contains(fields, static field => field.Name.Equals(":authority", StringComparison.Ordinal) && field.Value.Equals("svc.site.test", StringComparison.Ordinal));
        using var response = new MemoryStream();
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "200"), new("content-length", "2")]));
        Http3Codec.WriteFrame(response, Http3Codec.DataFrame, "ok"u8);
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }
}
