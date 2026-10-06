using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.INF.NoConf;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class RegisteredReadinessTests
{
    [Fact]
    public async Task ReadinessPinsTheEnrolledLiteralAndHonorsExplicitTlsAndSniSettingsAsync()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certificateRequest = new CertificateRequest("CN=backend.site.invalid", key, HashAlgorithmName.SHA256);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var names = new ConcurrentQueue<string?>();
        var server = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("ready"), certificate, names.Enqueue).ConfigureAwait(true);
        await using var serverLifetime = server.ConfigureAwait(true);
        var intent = Intent(server.Port, "https");
        var probe = new RegisteredReadinessProbe();
        Assert.False(await probe.CheckAsync(intent, Upstream(intent, new RuntimeUpstreamTlsOptions(true, "backend.site.invalid")), CancellationToken.None).ConfigureAwait(true));
        Assert.True(await probe.CheckAsync(intent, Upstream(intent, new RuntimeUpstreamTlsOptions(false, "backend.site.invalid")), CancellationToken.None).ConfigureAwait(true));
        Assert.Contains("backend.site.invalid", names, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ReadinessDoesNotFollowRedirectsOrInferAnotherHealthPathAsync()
    {
        var server = await DevelopmentHttpUpstream.StartAsync(context =>
        {
            if (string.Equals(context.Request.Path.Value, "/ready", StringComparison.Ordinal)) context.Response.Redirect("/different-health");
            else context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }).ConfigureAwait(true);
        await using var serverLifetime = server.ConfigureAwait(true);
        var intent = Intent(server.Port, "http");
        Assert.False(await new RegisteredReadinessProbe().CheckAsync(intent, Upstream(intent, RuntimeUpstreamTlsOptions.Default), CancellationToken.None).ConfigureAwait(true));
    }

    private static InstanceIntent Intent(int port, string scheme) => new(new RegisteredUpstreamIdentity("node", "owner", "svc", "v1", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")),
        "deployment", "127.0.0.1", port, "http1", scheme, "/ready", "local", 1, draining: false);
    private static RuntimeUpstream Upstream(InstanceIntent intent, RuntimeUpstreamTlsOptions tls) => new("svc", intent.Identity.InstanceId, intent.Scheme, intent.Protocol,
        intent.Address, intent.Port, 1, tls, RuntimeCircuitBreakerPolicy.Disabled, intent.Identity);
}
