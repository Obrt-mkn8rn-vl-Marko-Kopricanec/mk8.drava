using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Registration;
using Mk8.Drava.Transport.Discovery;
using Mk8.Drava.Transport.Registration;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class RegistrationSdkTests(ITestOutputHelper output)
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
        output.WriteLine("sdk_fixture: {0}", proxy.EvidenceId);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        using var registrationEvents = new DevelopmentRegistrationEvents();
        var multipleBindings = !automaticDiscovery && !customLifetimes;
        builder.WebHost.ConfigureKestrel(options =>
        {
            if (multipleBindings) options.Listen(IPAddress.Parse("127.0.0.2"), 0);
            options.Listen(IPAddress.Loopback, 0);
        });
        var declared = Options(proxy) with { AdvertiseAddress = "127.0.0.1" };
        var sdkOptions = customLifetimes ? declared with { PendingRetrySeconds = 1, RenewJitterPercent = 0, ShutdownDeadlineMilliseconds = 1500, AgentDrainDeadlineMilliseconds = 500 } : declared;
        builder.Services.AddDravaRegistration(automaticDiscovery ? sdkOptions with { MulticastDiscovery = true, GatewaySeeds = [] } : sdkOptions);
        var application = builder.Build();
        await using var applicationLifetime = application.ConfigureAwait(true);
        application.Run(context => context.Response.WriteAsync(string.Equals(context.Request.Path.Value, "/ready", StringComparison.Ordinal) ? "ready" : "sdk-body", context.RequestAborted));
        await application.StartAsync(CancellationToken.None).ConfigureAwait(true);
        var bound = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>() ?? throw new InvalidOperationException("Backend is not bound.");
        Assert.Equal(multipleBindings ? 2 : 1, bound.Addresses.Count);
        Assert.All(bound.Addresses, static address => Assert.True(new Uri(address).Port > 0));
        var state = application.Services.GetRequiredService<DravaRegistrationState>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (state.Status?.Phase != RegistrationPhase.Ready) await Task.Delay(100, timeout.Token).ConfigureAwait(true);
        Assert.NotEmpty(Assert.IsType<RegistrationStatus>(state.Status).AssignedUrls);
        if (customLifetimes) await AssertConfiguredLifetimesAsync(proxy, state, timeout.Token).ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var response = await client.Client.GetAsync(new Uri("/", UriKind.Relative), timeout.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("sdk-body", await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(true));
        await StopAndCaptureAsync(application, state, registrationEvents, output, timeout.Token).ConfigureAwait(true);
        Assert.NotEmpty(registrationEvents.Read());
        Assert.True(state.Status is { Phase: RegistrationPhase.Draining }, state.Failure);
        using var drained = await client.Client.GetAsync(new Uri("/", UriKind.Relative), timeout.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, drained.StatusCode);
    }

    private static async Task StopAndCaptureAsync(WebApplication application, DravaRegistrationState state, DevelopmentRegistrationEvents registrationEvents, ITestOutputHelper output, CancellationToken cancellationToken)
    {
        var stopStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var beforeStop = state.Status?.Phase;
        try { await application.StopAsync(cancellationToken).ConfigureAwait(true); }
        finally
        {
            output.WriteLine("sdk_stop: before={0}; after={1}; elapsed_ms={2}; caller_cancelled={3}", beforeStop, state.Status?.Phase,
                System.Diagnostics.Stopwatch.GetElapsedTime(stopStarted).TotalMilliseconds, cancellationToken.IsCancellationRequested);
            foreach (var service in application.Services.GetServices<IHostedService>())
            {
                if (service is BackgroundService background)
                    output.WriteLine("sdk_background_task: status={0}", background.ExecuteTask?.Status);
            }
            foreach (var entry in registrationEvents.Read()) output.WriteLine(entry);
        }
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
        var plan = await proxy.ReadGatewayPlanAsync(cancellationToken).ConfigureAwait(false);
        Assert.NotNull(plan);
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
            Site = new RegistrationSiteTrust { SiteId = "development", Domain = "site.test", RootFingerprint = root.GetCertHashString(HashAlgorithmName.SHA256), RootCertificate = new SecretBytes(File.ReadAllBytes(proxy.RootCertificatePath)), NodeCertificate = new SecretBytes(File.ReadAllBytes(proxy.NodeCertificatePath)), NodeAgentDescriptorPath = proxy.NodeAgentDescriptorPath },
            NodeId = "local", OwnerId = "development", ServiceId = "svc", ReadinessPath = "/ready", MulticastDiscovery = false,
            GatewaySeeds = [new DiscoveryCandidate("127.0.0.1", proxy.RegistrationPort)],
        };
    }
}
