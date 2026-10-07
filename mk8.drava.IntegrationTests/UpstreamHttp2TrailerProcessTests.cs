using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class UpstreamHttp2TrailerProcessTests
{
    [Fact]
    public async Task RequestAndResponseTrailersSurviveTheActualTwoProcessTlsHttp2PathAsync()
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        var observed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted).ConfigureAwait(false);
            observed.TrySetResult(context.Request.GetTrailer("x-client-end").ToString() + "|" + context.Request.Headers["TE"].ToString());
            context.Response.ContentType = "application/grpc";
            context.Response.ContentLength = body.Length;
            context.Response.DeclareTrailer("grpc-status");
            context.Response.DeclareTrailer("grpc-message");
            body.Position = 0;
            await body.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            context.Response.AppendTrailer("grpc-status", "0");
            context.Response.AppendTrailer("grpc-message", "complete%20here");
        }, certificate, http2: true).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var trust = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, proxy.TlsPort, deadline.Token).ConfigureAwait(true);
        var tls = new SslStream(socket.GetStream(), leaveInnerStreamOpen: true);
        await using var tlsLifetime = tls.ConfigureAwait(true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "svc.site.test",
            ApplicationProtocols = [SslApplicationProtocol.Http2], RemoteCertificateValidationCallback = trust.ValidateServer }, deadline.Token).ConfigureAwait(true);
        Assert.Equal(SslApplicationProtocol.Http2, tls.NegotiatedApplicationProtocol);
        await ExchangeEchoAndVerifyTrailersAsync(tls, deadline.Token).ConfigureAwait(true);
        Assert.Equal("client-complete|trailers", await observed.Task.WaitAsync(deadline.Token).ConfigureAwait(true));
    }

    private static async Task ExchangeEchoAndVerifyTrailersAsync(SslStream tls, CancellationToken cancellationToken)
    {
        var connection = new Http2UpstreamConnection(tls, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        await connection.InitializeAsync(timeouts, cancellationToken).ConfigureAwait(true);
        byte[] body = [0, 0, 0, 0, 3, 97, 98, 99];
        await connection.SendHeadersAsync([new(":method", "POST"), new(":scheme", "https"), new(":authority", "svc.site.test"),
            new(":path", "/echo"), new("content-type", "application/grpc"), new("te", "trailers")], false, timeouts, cancellationToken).ConfigureAwait(true);
        await connection.SendDataAsync(body, false, timeouts, cancellationToken).ConfigureAwait(true);
        await connection.SendHeadersAsync([new("x-client-end", "client-complete")], true, timeouts, cancellationToken).ConfigureAwait(true);
        var head = await connection.ReadResponseHeadAsync(16384, timeouts, cancellationToken).ConfigureAwait(true);
        Assert.Equal(200, head.StatusCode);
        using var received = new MemoryStream();
        Http2UpstreamDataChunk end;
        do
        {
            end = await connection.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(true);
            received.Write(end.Data);
        } while (!end.EndStream);
        Assert.Equal(body, received.ToArray());
        Assert.Contains(Assert.IsAssignableFrom<IReadOnlyList<Mk8.Drava.Application.BLL.Http.ProxyHeaderField>>(end.Trailers),
            static field => string.Equals(field.Name, "grpc-status", StringComparison.Ordinal) && string.Equals(field.Value, "0", StringComparison.Ordinal));
        Assert.Contains(end.Trailers!, static field => string.Equals(field.Name, "grpc-message", StringComparison.Ordinal)
            && string.Equals(field.Value, "complete%20here", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrailerBearingFixedLengthResponsesArePreservedAndExcludedFromTheBodyOnlyCacheAsync()
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        var calls = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            var call = Interlocked.Increment(ref calls);
            context.Response.ContentLength = 3;
            context.Response.Headers.CacheControl = "max-age=60";
            context.Response.DeclareTrailer("x-final-status");
            await context.Response.WriteAsync("abc", context.RequestAborted).ConfigureAwait(false);
            context.Response.AppendTrailer("x-final-status", call.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }, certificate, http2: true).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: true, manualCache: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        for (var index = 1; index <= 2; index++)
        {
            using var response = await client.Client.GetAsync(new Uri("/cached", UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("abc", await response.Content.ReadAsStringAsync().ConfigureAwait(true));
            var expected = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Collection(response.TrailingHeaders.GetValues("x-final-status"), value => Assert.Equal(expected, value));
        }
        Assert.Equal(2, Volatile.Read(ref calls));
    }
}
