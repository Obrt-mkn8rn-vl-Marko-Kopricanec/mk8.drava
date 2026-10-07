using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Registration;
using Mk8.Drava.Transport.Discovery;
using Mk8.Drava.Transport.Registration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class RegistrationSdkTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HostingSdkRegistersTheActualRandomBoundPortWithoutAProxyFileAndDrainsOnStopAsync(bool automaticDiscovery, bool customLifetimes)
    {
        var unused = TwoProcessProxy.UnusedPort();
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(unused, enrolledSite: true, manualRoute: false, dnsPort: dns.Port, discovery: automaticDiscovery, lifecycleSettings: customLifetimes ? DevelopmentLifecycleSettings.Fast : null).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var sdkOptions = customLifetimes ? Options(proxy) with { PendingRetrySeconds = 1, RenewJitterPercent = 0, ShutdownDeadlineMilliseconds = 1500, AgentDrainDeadlineMilliseconds = 500 } : Options(proxy);
        builder.Services.AddDravaRegistration(automaticDiscovery ? sdkOptions with { MulticastDiscovery = true, GatewaySeeds = [] } : sdkOptions);
        var application = builder.Build();
        await using var applicationLifetime = application.ConfigureAwait(true);
        application.Run(context => context.Response.WriteAsync(string.Equals(context.Request.Path.Value, "/ready", StringComparison.Ordinal) ? "ready" : "sdk-body"));
        await application.StartAsync().ConfigureAwait(true);
        var bound = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>() ?? throw new InvalidOperationException("Backend is not bound.");
        Assert.Collection(bound.Addresses, static value => Assert.True(new Uri(value).Port > 0));
        var state = application.Services.GetRequiredService<DravaRegistrationState>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (state.Status?.Phase != RegistrationPhase.Ready) await Task.Delay(100, timeout.Token).ConfigureAwait(true);
        Assert.NotEmpty(Assert.IsType<RegistrationStatus>(state.Status).AssignedUrls);
        if (customLifetimes) await AssertConfiguredLifetimesAsync(proxy, state, timeout.Token).ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var response = await client.Client.GetAsync(new Uri("/", UriKind.Relative), timeout.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("sdk-body", await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(true));
        await application.StopAsync(timeout.Token).ConfigureAwait(true);
        Assert.True(state.Status is { Phase: RegistrationPhase.Draining }, state.Failure);
        using var drained = await client.Client.GetAsync(new Uri("/", UriKind.Relative), timeout.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, drained.StatusCode);
    }

    [Fact]
    public async Task ADiscoveredLocatorCannotReplaceThePinnedSiteOrUseAnotherSiteNameAsync()
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), enrolledSite: true, manualRoute: false).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var options = Options(proxy);
        Assert.Throws<InvalidDataException>(() => new SiteRegistrationChannel(options.Site with { RootFingerprint = new string('A', 64) }, new DiscoveryCandidate("127.0.0.1", proxy.RegistrationPort)));
        using var wrongName = new SiteRegistrationChannel(options.Site with { Domain = "different.test" }, new DiscoveryCandidate("127.0.0.1", proxy.RegistrationPort));
        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => wrongName.VerifySiteAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    private static async Task AssertConfiguredLifetimesAsync(TwoProcessProxy proxy, DravaRegistrationState state, CancellationToken cancellationToken)
    {
        Assert.Equal(15, state.Status!.LeaseSeconds);
        Assert.Equal(4, state.Status.RenewAfterSeconds);
        var plan = Mk8.Drava.Transport.Protocol.V1.PresentationPlan.Parser.ParseFrom(await File.ReadAllBytesAsync(proxy.GatewayPlanPath, cancellationToken).ConfigureAwait(false));
        Assert.Equal(6U, plan.AcknowledgmentLeaseSeconds);
        Assert.Equal(7U, plan.LeafLifetimeDays);
        await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
        Assert.Equal(RegistrationPhase.Ready, state.Status?.Phase);
    }

    private static DravaRegistrationOptions Options(TwoProcessProxy proxy)
    {
        using var root = X509CertificateLoader.LoadCertificateFromFile(proxy.RootCertificatePath);
        return new DravaRegistrationOptions
        {
            Site = new RegistrationSiteTrust { SiteId = "development", Domain = "site.test", RootFingerprint = root.GetCertHashString(HashAlgorithmName.SHA256), RootCertificatePath = proxy.RootCertificatePath, NodeCertificatePath = proxy.NodeCertificatePath },
            NodeId = "local", OwnerId = "development", ServiceId = "svc", ReadinessPath = "/ready", MulticastDiscovery = false,
            GatewaySeeds = [new DiscoveryCandidate("127.0.0.1", proxy.RegistrationPort)],
        };
    }
}
