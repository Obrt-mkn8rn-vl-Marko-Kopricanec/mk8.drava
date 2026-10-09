using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.INF.Acme;
using Mk8.Drava.Configuration;
using Mk8.Drava.Gateway.Hosting;
using Mk8.Drava.Transport.Certificates;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.UnitTests;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class AcmeHostedPipelineTests
{
    [Fact]
    public async Task ActualGatewayFetchesAcknowledgesAndServesSignedIssuedMaterialOverPrivateIpcAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        var dns = new DevelopmentAcmeDnsProvider();
        using var server = new DevelopmentAcmeServer(dns, Path.Combine(fixture.Application.StateDirectory, "account.pem"));
        using var publicRoot = X509CertificateLoader.LoadCertificate(server.RootCertificate);
        var directory = fixture.Application.StateDirectory;
        var (bootstrap, gateway) = await PreparePublicationGatewayAsync(fixture, server, publicRoot).ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, TimeProvider.System, CancellationToken.None).ConfigureAwait(true);
        var pending = plans.Read("local"); Assert.True(pending.ServingPending);
        var gatewayPath = await PreparePendingGatewayAsync(gateway, pending, directory).ConfigureAwait(true);
        var process = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Gateway"), gatewayPath);
        await using var processLifetime = process.ConfigureAwait(true);
        var host = DevelopmentControlPlanHost.Build(bootstrap, plans);
        await using var hostLifetime = host.ConfigureAwait(true);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await PublicServingChainProcessTests.WaitForListenerAsync(process, gateway.HttpsPort, deadline.Token).ConfigureAwait(true);
            using var privateRoot = fixture.Authority.PublicCertificate;
            using var node = fixture.Authority.IssueNode("node", ["127.0.0.1"]);
            await AssertPendingTlsAsync(bootstrap, gateway, privateRoot, node, deadline.Token).ConfigureAwait(true);
            var current = await IssueServingMaterialAsync(bootstrap, plans, dns, server, pending, deadline.Token).ConfigureAwait(true);
            await host.StartAsync(deadline.Token).ConfigureAwait(true);
            await AssertControlRejectionsAsync(bootstrap.Listen, pending, deadline.Token).ConfigureAwait(true);
            while (plans.ReadPublicationProof() is null)
            {
                process.ThrowIfExited();
                await Task.Delay(50, deadline.Token).ConfigureAwait(true);
            }
            Assert.Equal(checked((long)current.Generation), plans.ReadPublicationProof()!.Generation);
            var cached = PresentationPlan.Parser.ParseFrom(await File.ReadAllBytesAsync(Path.Combine(gateway.StateDirectory, "serving.plan"), deadline.Token).ConfigureAwait(true));
            Assert.Equal(current.Generation, cached.Generation); Assert.Equal(current.ContentSha256, cached.ContentSha256);
            await AssertIssuedTlsAsync(gateway, current, publicRoot, privateRoot, node, deadline.Token).ConfigureAwait(true);
        }
        finally
        {
            await process.DisposeAsync().ConfigureAwait(true);
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(shutdown.Token).ConfigureAwait(true);
            var evidence = Path.Combine(TwoProcessProxy.FindRoot(), "artifacts", "acme-gateway-publication-tests", Path.GetFileName(directory));
            Directory.CreateDirectory(evidence);
            await File.WriteAllTextAsync(Path.Combine(evidence, "gateway.log"), process.CapturedLog).ConfigureAwait(true);
        }
    }

    private static async Task<string> PreparePendingGatewayAsync(GatewayBootstrap gateway, PresentationPlan pending, string directory)
    {
        using (var cache = new GatewayPlanCache(gateway.StateDirectory))
            await cache.WriteAsync(pending, CancellationToken.None).ConfigureAwait(true);
        var gatewayPath = Path.Combine(directory, "gateway.json");
        await File.WriteAllTextAsync(gatewayPath, BootstrapFile.Serialize(gateway)).ConfigureAwait(true);
        return gatewayPath;
    }

    private static async Task AssertPendingTlsAsync(ApplicationBootstrap bootstrap, GatewayBootstrap gateway,
        X509Certificate2 privateRoot, X509Certificate2 node, CancellationToken cancellationToken)
    {
        var privateBefore = await PublicServingChainProcessTests.ReadCertificateAsync(gateway.RegistrationPort, "register.site.test", privateRoot, node, requireIntermediate: false, cancellationToken).ConfigureAwait(true);
        using var pendingClient = new DevelopmentSiteClient(bootstrap.Controller!.Acme.PinnedServingRootPath, gateway.HttpsPort, "svc.site.test");
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            using var response = await pendingClient.Client.GetAsync(new Uri("/_drava/live", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(true);
        Assert.False(string.IsNullOrEmpty(privateBefore));
    }

    private static async Task<PresentationPlan> IssueServingMaterialAsync(ApplicationBootstrap bootstrap, ServingPlanState plans,
        DevelopmentAcmeDnsProvider dns, DevelopmentAcmeServer server, PresentationPlan pending, CancellationToken cancellationToken)
    {
        var directory = bootstrap.StateDirectory;
        var repository = await SqliteRegistryRepository.OpenAsync(directory, "site", cancellationToken).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var lifecycle = new AcmeServingLifecycle(bootstrap, plans); var history = History(repository, bootstrap);
        var counters = new LifecycleCounters(); using var issuer = Issuer(bootstrap, dns, server);
        await Manager(bootstrap, lifecycle, issuer, new AcmeCertificateStatusStore(history), counters).CheckRenewalsAsync(cancellationToken).ConfigureAwait(true);
        Assert.Equal(1, counters.Succeeded); Assert.Equal(0, counters.Failed); Assert.Equal(1, server.Finalizations);
        Assert.Equal(2, dns.Published); Assert.Equal(2, dns.Removed); Assert.Empty(dns.Records);
        var current = plans.Read("local"); Assert.False(current.ServingPending); Assert.Equal(pending.Generation + 1, current.Generation);
        Assert.Null(plans.ReadPublicationProof());
        Assert.Equal("succeeded", RequireOne(await history.ReadAsync(cancellationToken).ConfigureAwait(true)).LastResult);
        return current;
    }

    private static async Task<(ApplicationBootstrap Application, GatewayBootstrap Gateway)> PreparePublicationGatewayAsync(
        DevelopmentServingPlanFixture fixture, DevelopmentAcmeServer server, X509Certificate2 publicRoot)
    {
        var directory = fixture.Application.StateDirectory;
        var tokenPath = Path.Combine(directory, "gateway.token");
        await WriteProtectedAsync(tokenPath, System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))).ConfigureAwait(true);
        var ipc = OperatingSystem.IsWindows()
            ? new IpcEndpoint { NamedPipeName = "drava_" + Guid.NewGuid().ToString("N"), IdentityTokenPath = tokenPath }
            : new IpcEndpoint { UnixSocketPath = Path.Combine(directory, "app.sock"), IdentityTokenPath = tokenPath };
        var gateway = fixture.Gateway with
        {
            StateDirectory = Path.Combine(directory, "gateway"), Application = ipc, DiscoveryEnabled = false,
            ServingTrust = new ServingTrustSettings { Mode = "pinned", RootFingerprint = publicRoot.GetCertHashString(HashAlgorithmName.SHA256) },
            HttpPort = TwoProcessProxy.UnusedPort(), HttpsPort = TwoProcessProxy.UnusedPort(),
            RegistrationPort = TwoProcessProxy.UnusedPort(), ManagementPort = TwoProcessProxy.UnusedPort(),
        };
        var bootstrap = PublicBootstrap(fixture, server) with
        {
            Listen = ipc, HttpPort = gateway.HttpPort, HttpsPort = gateway.HttpsPort, ManagementPort = gateway.ManagementPort,
        };
        bootstrap = bootstrap with { Controller = bootstrap.Controller! with { RegistrationPort = gateway.RegistrationPort } };
        await WriteProtectedAsync(bootstrap.Controller.Acme.PinnedServingRootPath, server.RootCertificate).ConfigureAwait(true);
        return (bootstrap, gateway);
    }

    private static async Task AssertIssuedTlsAsync(GatewayBootstrap gateway, PresentationPlan current, X509Certificate2 publicRoot,
        X509Certificate2 privateRoot, X509Certificate2 node, CancellationToken cancellationToken)
    {
        using var issued = X509CertificateLoader.LoadPkcs12(current.Certificates[0].Pfx.Span, null, X509KeyStorageFlags.EphemeralKeySet);
        var serving = await PublicServingChainProcessTests.ReadCertificateAsync(gateway.HttpsPort, "svc.site.test", publicRoot, clientCertificate: null, requireIntermediate: true, cancellationToken).ConfigureAwait(true);
        Assert.Equal(issued.GetCertHashString(HashAlgorithmName.SHA256), serving);
        var privateAfter = await PublicServingChainProcessTests.ReadCertificateAsync(gateway.RegistrationPort, "register.site.test", privateRoot, node, requireIntermediate: false, cancellationToken).ConfigureAwait(true);
        Assert.NotEqual(serving, privateAfter, StringComparer.Ordinal);
        var management = await PublicServingChainProcessTests.ReadCertificateAsync(gateway.ManagementPort, "admin.site.test", privateRoot, node, requireIntermediate: false, cancellationToken).ConfigureAwait(true);
        Assert.Equal(privateAfter, management);
    }

    private static async Task AssertControlRejectionsAsync(IpcEndpoint ipc, PresentationPlan pending, CancellationToken cancellationToken)
    {
        using var channel = new ApplicationChannel(ipc);
        var control = new ApplicationControl.ApplicationControlClient(channel.Invoker);
        var unauthorized = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            using var call = control.GatewayPlanAsync(new GatewayIdentity { Version = 1, GatewayId = "local" }, cancellationToken: cancellationToken);
            await call.ResponseAsync.ConfigureAwait(false);
        }).ConfigureAwait(false);
        Assert.Equal(StatusCode.Unauthenticated, unauthorized.StatusCode);
        var stale = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            using var call = control.AcknowledgePlanAsync(new PlanAcknowledgment
            {
                Version = 1, GatewayId = pending.GatewayId, Generation = pending.Generation, ContentSha256 = pending.ContentSha256, Applied = true,
            }, channel.Credentials, cancellationToken: cancellationToken);
            await call.ResponseAsync.ConfigureAwait(false);
        }).ConfigureAwait(false);
        Assert.Equal(StatusCode.InvalidArgument, stale.StatusCode);
    }

    [Fact]
    public async Task SignedIssuerPublishesThroughImportedManagerAndRequiresExactGatewayAcknowledgmentAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        var dns = new DevelopmentAcmeDnsProvider();
        using var server = new DevelopmentAcmeServer(dns, Path.Combine(fixture.Application.StateDirectory, "account.pem"));
        var bootstrap = PublicBootstrap(fixture, server);
        await WriteProtectedAsync(bootstrap.Controller!.Acme.PinnedServingRootPath, server.RootCertificate).ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, TimeProvider.System, CancellationToken.None).ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(bootstrap.StateDirectory, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var lifecycle = new AcmeServingLifecycle(bootstrap, plans); var history = History(repository, bootstrap);
        var status = new AcmeCertificateStatusStore(history); var counters = new LifecycleCounters();
        using var issuer = Issuer(bootstrap, dns, server);
        var manager = Manager(bootstrap, lifecycle, issuer, status, counters);
        var pending = plans.Read("local"); Assert.True(pending.ServingPending);
        await manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, counters.Succeeded); Assert.Equal(0, counters.Failed); Assert.Equal(1, server.Finalizations);
        Assert.Equal(2, dns.Published); Assert.Equal(2, dns.Removed); Assert.Empty(dns.Records);
        var current = plans.Read("local"); Assert.False(current.ServingPending); Assert.Equal(pending.Generation + 1, current.Generation);
        Assert.Null(plans.ReadPublicationProof());
        using var validated = new ValidatedServingPlan(current, fixture.Gateway with { ServingTrust = bootstrap.Controller.ServingTrust }, TimeProvider.System, requireCurrent: true);
        Assert.True(validated.HasServingCertificate);
        var persisted = RequireOne(await history.ReadAsync(CancellationToken.None).ConfigureAwait(true)); Assert.Equal("succeeded", persisted.LastResult);
        Assert.True(persisted.NextAttemptNotBeforeUtc > DateTimeOffset.UtcNow.AddDays(3));
        Assert.True(plans.Acknowledge(new PlanAcknowledgment { Version = 1, GatewayId = current.GatewayId, Generation = current.Generation, ContentSha256 = current.ContentSha256, Applied = true }));
        Assert.NotNull(plans.ReadPublicationProof());
        await manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, server.Finalizations); Assert.Equal("not-due", status.Get("site")!.LastResult);
    }

    [Fact]
    public async Task RenewalServiceStopJoinsBlockedSignedIssuerProofAndExactCleanupBeforeDisposalAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        var dns = new DevelopmentAcmeDnsProvider { BlockProof = true };
        using var server = new DevelopmentAcmeServer(dns, Path.Combine(fixture.Application.StateDirectory, "account.pem"));
        var bootstrap = PublicBootstrap(fixture, server);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, TimeProvider.System, CancellationToken.None).ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(bootstrap.StateDirectory, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var lifecycle = new AcmeServingLifecycle(bootstrap, plans); var history = History(repository, bootstrap);
        var counters = new LifecycleCounters(); using var issuer = Issuer(bootstrap, dns, server);
        using var service = new AcmeRenewalService(lifecycle, Manager(bootstrap, lifecycle, issuer, new AcmeCertificateStatusStore(history), counters),
            new AcmeRenewalSchedulePolicy(), TimeProvider.System, NullLogger<AcmeRenewalService>.Instance);
        await service.StartAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            await dns.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            Assert.Equal("attempting", RequireOne(await history.ReadAsync(CancellationToken.None).ConfigureAwait(true)).LastResult);
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await service.StopAsync(shutdown.Token).ConfigureAwait(true);
        }
        Assert.NotNull(service.ExecuteTask); Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
        Assert.True(dns.Exited.Task.IsCompletedSuccessfully); Assert.Equal(1, dns.Removed); Assert.Empty(dns.Records);
        Assert.Equal(0, counters.Succeeded); Assert.Equal(0, server.Finalizations); Assert.True(plans.Read("local").ServingPending);
    }

    [Fact]
    public async Task EnabledCompositionSharesRuntimePortsAndDisposalReleasesOwnedJournalAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var material = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, material);
        await WriteProtectedAsync(bootstrap.Controller!.DnsPublication.CredentialPath, System.Text.Encoding.ASCII.GetBytes(new string('A', 48))).ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(bootstrap.StateDirectory, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var services = Services(bootstrap); services.AddOwnerAcmeLifecycle(bootstrap, plans, History(repository, bootstrap));
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(true))
        {
            var lifecycle = provider.GetRequiredService<IAcmeRenewalConfigurationSource>();
            Assert.Same(lifecycle, provider.GetRequiredService<IAcmeCertificateMaterialWriter>());
            Assert.Same(lifecycle, provider.GetRequiredService<IAcmeCertificateActivator>());
            Assert.Same(lifecycle, provider.GetRequiredService<IAcmeRenewalScheduleInputSource>());
            Assert.IsType<OwnedDns01CertificateIssuer>(provider.GetRequiredService<IAcmeCertificateIssuer>());
            Assert.NotNull(provider.GetRequiredService<AcmeCertificateManager>());
            Assert.Collection(provider.GetServices<IHostedService>().OfType<AcmeRenewalService>(), static service => Assert.NotNull(service));
            Assert.False(File.Exists(bootstrap.Controller.Acme.AccountKeyPath));
        }
        using var reopened = new AcmeDns01CleanupJournal(bootstrap.Controller.Acme.CleanupJournalPath);
        Assert.Empty(reopened.Read());
    }

    [Fact]
    public async Task DisabledCompositionDoesNotOpenOwnerIssuerMaterialOrRegisterRenewalServiceAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        var bootstrap = fixture.Application;
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(bootstrap.StateDirectory, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var services = Services(bootstrap); services.AddOwnerAcmeLifecycle(bootstrap, plans, History(repository, bootstrap));
        var provider = services.BuildServiceProvider(); await using var providerLifetime = provider.ConfigureAwait(true);
        Assert.IsType<DisabledAcmeCertificateIssuer>(provider.GetRequiredService<IAcmeCertificateIssuer>());
        Assert.Empty(provider.GetServices<IHostedService>().OfType<AcmeRenewalService>());
        Assert.Null(provider.GetService<IAcmeCertificateStatusPersistence>());
    }

    private static ApplicationBootstrap PublicBootstrap(DevelopmentServingPlanFixture fixture, DevelopmentAcmeServer server)
    {
        using var root = X509CertificateLoader.LoadCertificate(server.RootCertificate);
        var directory = fixture.Application.StateDirectory;
        return fixture.Application with { Controller = fixture.Application.Controller! with
        {
            ServingTrust = new ServingTrustSettings { Mode = "pinned", RootFingerprint = root.GetCertHashString(HashAlgorithmName.SHA256) },
            ServingCertificatePath = Path.Combine(directory, "public.pfx"),
            DnsPublication = new DnsPublicationSettings { Provider = "cloudflare", ZoneId = new string('a', 32), ZoneName = "site.test", CredentialPath = Path.Combine(directory, "dns.token") },
            Acme = new AcmeIssuanceSettings { Enabled = true, TermsAccepted = true, DirectoryUrl = new Uri("https://ca.example/directory"), ContactEmails = ["ops@example.org"],
                AccountKeyPath = Path.Combine(directory, "account.pem"), CleanupJournalPath = Path.Combine(directory, "cleanup.json"), PinnedServingRootPath = Path.Combine(directory, "public-root.der") },
        } };
    }

    private static CertesDns01CertificateIssuer Issuer(ApplicationBootstrap bootstrap, DevelopmentAcmeDnsProvider dns, DevelopmentAcmeServer server) => new(
        new AcmeDns01IssuerPolicy { SiteDomain = bootstrap.Controller!.Domain, Directory = bootstrap.Controller.Acme.DirectoryUrl,
            AccountKeyPath = bootstrap.Controller.Acme.AccountKeyPath, ContactEmails = bootstrap.Controller.Acme.ContactEmails, TermsAccepted = true, PollInterval = TimeSpan.FromMilliseconds(100) }, dns, () => server);
    private static SqliteAcmeCertificateStatusPersistence History(SqliteRegistryRepository repository, ApplicationBootstrap bootstrap) =>
        new(repository, bootstrap.Controller!.Domain, bootstrap.Controller.Acme.DirectoryUrl);
    private static AcmeCertificateManager Manager(ApplicationBootstrap bootstrap, AcmeServingLifecycle lifecycle, IAcmeCertificateIssuer issuer, AcmeCertificateStatusStore status, LifecycleCounters counters) =>
        new(lifecycle, lifecycle, new ApplicationDataDirectoryProvider(bootstrap.StateDirectory), issuer, lifecycle, new AcmeChallengeStore(), status, TimeProvider.System, counters, counters);
    internal static ServiceCollection Services(ApplicationBootstrap bootstrap)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddProxyApplication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Mdrava:DataDirectory"] = bootstrap.StateDirectory }).Build());
        services.AddSingleton<IMdravaDataDirectoryProvider>(new ApplicationDataDirectoryProvider(bootstrap.StateDirectory)); return services;
    }
    private static async Task WriteProtectedAsync(string path, byte[] bytes)
    {
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    private static AcmeCertificateLifecycleStatus RequireOne(IReadOnlyList<AcmeCertificateLifecycleStatus> statuses)
    {
        Assert.Collection(statuses, static status => Assert.NotNull(status));
        return statuses[0];
    }
    private sealed class LifecycleCounters : IProxyAcmeMetricsSink, IAcmeCertificateRenewalEventSink
    {
        public int Succeeded { get; private set; }
        public int Failed { get; private set; }
        public void AcmeRenewalAttempted() { }
        public void AcmeRenewalSucceeded() => Succeeded++;
        public void AcmeRenewalFailed() => Failed++;
        public void RenewalFailed(string certificateId, string? errorSummary) { }
    }
}
