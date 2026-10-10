using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.UnitTests;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class VerifiedUpstreamReadinessTests
{
    [Theory]
    [InlineData("matching")]
    [InlineData("wrong-name")]
    [InlineData("expired")]
    [InlineData("system-trust")]
    public async Task RegistrationReadinessUsesTheForwardingTrustPolicyAndPinnedLiteralAsync(string profile)
    {
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create(expired: string.Equals(profile, "expired", StringComparison.Ordinal));
        var rootPath = Path.Combine(directory.Path, "upstream-root.cer");
        await File.WriteAllBytesAsync(rootPath, certificates.Root.RawData).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(rootPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var root = string.Equals(profile, "system-trust", StringComparison.Ordinal) ? null
            : new RuntimeTrustedRootCertificate(rootPath, certificates.Root.GetCertHashString(HashAlgorithmName.SHA256));
        var requests = 0;
        var names = new ConcurrentQueue<string?>();
        var upstream = await DevelopmentHttpUpstream.StartAsync(context =>
        {
            Interlocked.Increment(ref requests);
            Assert.True(context.Request.IsHttps);
            Assert.Equal("/health/ready", context.Request.Path.Value);
            Assert.Equal("backend.drava.invalid", context.Request.Host.Host);
            return context.Response.WriteAsync("ready", context.RequestAborted);
        }, certificates.Leaf, names.Enqueue).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var intent = new InstanceIntent(new RegisteredUpstreamIdentity("node", "owner", "svc", "v1", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")),
            "deployment", "127.0.0.1", upstream.Port, "http1", "https", "/health/ready", "local", 1, draining: false);
        var backend = new RuntimeUpstream("svc", intent.Identity.InstanceId, intent.Scheme, intent.Protocol, intent.Address, intent.Port, 1,
            new RuntimeUpstreamTlsOptions(true, string.Equals(profile, "wrong-name", StringComparison.Ordinal) ? "other.drava.invalid" : "backend.drava.invalid", root),
            RuntimeCircuitBreakerPolicy.Disabled, intent.Identity);
        var ready = await new RegisteredReadinessProbe().CheckAsync(intent, backend, CancellationToken.None).ConfigureAwait(true);
        var expected = string.Equals(profile, "matching", StringComparison.Ordinal);
        Assert.Equal(expected, ready);
        Assert.Equal(expected ? 1 : 0, Volatile.Read(ref requests));
        Assert.Contains(backend.EffectiveSniHost, names, StringComparer.Ordinal);
    }
}
