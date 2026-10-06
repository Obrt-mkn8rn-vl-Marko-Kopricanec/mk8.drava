using System.Net;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayTlsTests
{
    [Fact]
    public async Task SeparateGatewayBindsItsRealTlsPlanAndProxiesHttp2Async()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            await context.Request.Body.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        var body = Enumerable.Range(0, 200_000).Select(static index => (byte)(index % 251)).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/secure") { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact, Content = new ByteArrayContent(body) };
        using var response = await client.Client.SendAsync(request).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(body, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
        using var channel = new ApplicationChannel(proxy.Ipc);
        var control = new ApplicationControl.ApplicationControlClient(channel.Invoker);
        using var planCall = control.GatewayPlanAsync(new GatewayIdentity { Version = 1, GatewayId = "local" }, channel.Credentials);
        var plan = await planCall.ResponseAsync.ConfigureAwait(true);
        Assert.True(PresentationPlanDigest.Verify(plan));
        using var invalid = control.AcknowledgePlanAsync(new PlanAcknowledgment { Version = 1, GatewayId = "local", Generation = 1, Applied = true, ContentSha256 = ByteString.CopyFrom(new byte[32]) }, channel.Credentials);
        RpcException? failure = null;
        try { await invalid.ResponseAsync.ConfigureAwait(true); }
        catch (RpcException exception) { failure = exception; }
        Assert.NotNull(failure);
        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
    }

    [Fact]
    public async Task RegistrationListenerRejectsAnAnonymousTlsPeerAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("unused")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.RegistrationPort, "register.site.test");
        await Assert.ThrowsAsync<HttpRequestException>(async () => await client.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
    }
}
