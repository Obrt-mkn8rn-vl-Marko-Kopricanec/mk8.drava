using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Registration;
using Mk8.Drava.Application.DAL.Installation;
using Mk8.Drava.Transport.Registration;
using Mk8.Drava.Transport.Discovery;
using Grpc.Core;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class SiteInitializationTests
{
    private static readonly byte[] SetupPayload = [1, 2, 3];
    [Fact]
    public async Task GeneratedSiteBootstrapsEnrollAndRouteAnSdkHostAndRetainTrustOnRestartAsync()
    {
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var site = new DevelopmentInitializedSite(dns.Port);
        await using var siteLifetime = site.ConfigureAwait(true);
        Assert.Equal(0, await site.InitializeAsync().ConfigureAwait(true));
        AssertPrivateFiles(site.DirectoryPath);
        var profile = await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(site.EnrollmentPath, CancellationToken.None).ConfigureAwait(true);
        Assert.Contains(profile.Site.RootFingerprint, site.InitializationLog, StringComparison.Ordinal);
        var originalRoot = profile.Site.RootCertificate;
        var originalNode = profile.Site.NodeCertificate;
        await site.StartAsync().ConfigureAwait(true);
        var options = DravaRegistrationOptions.FromEnrollment(await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(site.EnrollmentPath, CancellationToken.None).ConfigureAwait(true), "svc-api", "/ready");
        options = options with { PendingRetrySeconds = 1, RenewJitterPercent = 0 };
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("initialized-service", context.RequestAborted), registration: options).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        await WaitReadyAsync(upstream).ConfigureAwait(true);
        var gateway = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(site.GatewayPath, CancellationToken.None).ConfigureAwait(true);
        using var business = new DevelopmentSiteClient(profile.Site.RootCertificate, gateway.HttpsPort, "svc-api.site.test");
        await AssertServedAsync(business.Client).ConfigureAwait(true);
        await AssertInitialGrantAsync(site, gateway, profile).ConfigureAwait(true);
        var beforeRestart = await site.ReadCurrentApplicationPlanAsync().ConfigureAwait(true);
        await site.RestartApplicationAsync().ConfigureAwait(true);
        var afterRestart = await site.ReadCurrentApplicationPlanAsync().ConfigureAwait(true);
        Assert.Equal(beforeRestart.Generation, afterRestart.Generation);
        Assert.Equal(beforeRestart.ContentSha256, afterRestart.ContentSha256);
        await AssertServedAsync(business.Client).ConfigureAwait(true);
        await AssertInitialGrantAsync(site, gateway, profile).ConfigureAwait(true);
        Assert.Equal(originalRoot, (await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(site.EnrollmentPath, CancellationToken.None).ConfigureAwait(true)).Site.RootCertificate);
        Assert.Equal(originalNode, (await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(site.EnrollmentPath, CancellationToken.None).ConfigureAwait(true)).Site.NodeCertificate);
        Assert.NotEqual(0, await site.InitializeAsync().ConfigureAwait(true));
        Assert.Equal(originalRoot, (await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(site.EnrollmentPath, CancellationToken.None).ConfigureAwait(true)).Site.RootCertificate);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("portCollision")]
    [InlineData("badScope")]
    [InlineData("longSocket")]
    public async Task InvalidInitializationNeverCreatesAPartialSiteAsync(string fault)
    {
        var site = new DevelopmentInitializedSite();
        await using var lifetime = site.ConfigureAwait(true);
        var json = site.Options.ToJsonString();
        switch (fault)
        {
            case "unknown": site.Options["unrecognized"] = true; json = site.Options.ToJsonString(); break;
            case "duplicate": json = json.Insert(1, "\"schemaVersion\":1,\"schemaVersion\":1,"); break;
            case "null": site.Options["endpointAddresses"] = null; json = site.Options.ToJsonString(); break;
            case "portCollision": site.Options["registrationPort"] = site.Options["httpsPort"]!.GetValue<int>(); json = site.Options.ToJsonString(); break;
            case "badScope": site.Options["endpointAddresses"] = new System.Text.Json.Nodes.JsonArray("0.0.0.0"); json = site.Options.ToJsonString(); break;
            case "longSocket": site.Options["destinationDirectory"] = site.DirectoryPath + new string('a', 100); json = site.Options.ToJsonString(); break;
        }
        Assert.NotEqual(0, await site.InitializeAsync(json).ConfigureAwait(true));
        Assert.False(Directory.Exists(site.DirectoryPath));
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(site.OptionsPath)!, ".drava-init-*"));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("file")]
    [InlineData("symlink")]
    [InlineData("sharedParent")]
    public async Task InitializationRefusesExistingOrUnprotectedLocationsAsync(string fault)
    {
        var site = new DevelopmentInitializedSite();
        await using var lifetime = site.ConfigureAwait(true);
        var sentinel = Path.Combine(Path.GetDirectoryName(site.OptionsPath)!, "sentinel");
        await File.WriteAllTextAsync(sentinel, "keep").ConfigureAwait(true);
        if (string.Equals(fault, "directory", StringComparison.Ordinal)) Directory.CreateDirectory(site.DirectoryPath);
        else if (string.Equals(fault, "file", StringComparison.Ordinal)) await File.WriteAllTextAsync(site.DirectoryPath, "keep").ConfigureAwait(true);
        else if (string.Equals(fault, "symlink", StringComparison.Ordinal)) File.CreateSymbolicLink(site.DirectoryPath, sentinel);
        else if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.GetDirectoryName(site.OptionsPath)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);
        Assert.NotEqual(0, await site.InitializeAsync().ConfigureAwait(true));
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel).ConfigureAwait(true));
        if (string.Equals(fault, "file", StringComparison.Ordinal)) Assert.Equal("keep", await File.ReadAllTextAsync(site.DirectoryPath).ConfigureAwait(true));
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(site.OptionsPath)!, ".drava-init-*"));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("service")]
    [InlineData("port")]
    public async Task TheGeneratedGrantRejectsSignedCommandsOutsideItsScopeAsync(string scope)
    {
        var site = new DevelopmentInitializedSite();
        await using var lifetime = site.ConfigureAwait(true);
        if (string.Equals(scope, "port", StringComparison.Ordinal)) { site.Options["minimumPort"] = 100; site.Options["maximumPort"] = 100; }
        Assert.Equal(0, await site.InitializeAsync().ConfigureAwait(true));
        await site.StartAsync().ConfigureAwait(true);
        var profile = await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(site.EnrollmentPath, CancellationToken.None).ConfigureAwait(true);
        using var channel = new SiteRegistrationChannel(profile.Site, new DiscoveryCandidate(profile.GatewayAddresses[0], profile.RegistrationPort));
        await channel.VerifySiteAsync(CancellationToken.None).ConfigureAwait(true);
        var identity = new RegistrationIdentity
        {
            SiteId = profile.Site.SiteId, NodeId = profile.NodeId, OwnerId = string.Equals(scope, "owner", StringComparison.Ordinal) ? "other" : profile.OwnerId,
            ServiceId = string.Equals(scope, "service", StringComparison.Ordinal) ? "other" : "svc-api", ContractId = "v1",
            InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N"),
        };
        var command = new RegistrationCommand { Operation = RegistrationOperation.Register, Identity = identity,
            Advertisement = new ServiceAdvertisement { DeploymentId = "default", Address = "127.0.0.1", Port = TwoProcessProxy.UnusedPort(), ReadinessPath = "/ready", }, };
        var rejected = await Assert.ThrowsAsync<RpcException>(() => channel.SubmitAsync(command, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(StatusCode.PermissionDenied, rejected.StatusCode);
    }

    [Fact]
    public async Task FailedPublicationRemovesOnlyItsWorkspaceAndPreservesAForeignDestinationAsync()
    {
        var site = new DevelopmentInitializedSite();
        await using var lifetime = site.ConfigureAwait(true);
        var workspace = PrivateSiteWorkspace.Create(site.DirectoryPath);
        using (workspace)
        {
            await workspace.WriteAsync("secret", SetupPayload, CancellationToken.None).ConfigureAwait(true);
            Directory.CreateDirectory(site.DirectoryPath);
            await File.WriteAllTextAsync(Path.Combine(site.DirectoryPath, "foreign"), "keep").ConfigureAwait(true);
            Assert.Throws<IOException>(workspace.Publish);
        }
        Assert.False(Directory.Exists(workspace.StagingDirectory));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(site.DirectoryPath, "foreign")).ConfigureAwait(true));
    }

    [Fact]
    public async Task ProfileSdkAcceptsTheConfiguredPublishedHostnameAsync()
    {
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var site = new DevelopmentInitializedSite(dns.Port);
        await using var siteLifetime = site.ConfigureAwait(true);
        Assert.Equal(0, await site.InitializeAsync().ConfigureAwait(true));
        await site.StartAsync().ConfigureAwait(true);
        var options = DravaRegistrationOptions.FromEnrollment(await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(site.EnrollmentPath, CancellationToken.None).ConfigureAwait(true), "svc-api", "/ready");
        var gateway = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(site.GatewayPath, CancellationToken.None).ConfigureAwait(true);
        using var administrator = new DevelopmentSiteClient(options.Site.RootCertificate, gateway.ManagementPort, "admin.site.test", options.Site.NodeCertificate);
        administrator.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await PortableConfiguration.LoadDocumentAsync<ApplicationBootstrap>(site.ApplicationPath, CancellationToken.None).ConfigureAwait(true)).AdministratorToken.RequireToken());
        var policy = await administrator.Client.GetFromJsonAsync<PolicyStateResponse>(new Uri("/admin/drava/policy", UriKind.Relative)).ConfigureAwait(true);
        Assert.NotNull(policy);
        using var accepted = await administrator.Client.PostAsJsonAsync(new Uri("/admin/drava/policy", UriKind.Relative), new PolicyUpdateRequest
        {
            ExpectedRevision = policy.AcceptedRevision,
            Policy = JsonSerializer.SerializeToElement(new { services = new[] { new { serviceId = "svc-api", profile = new { host = "edge.site.test", pathPrefix = "/api/" } } } }),
        }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("initialized-service", context.RequestAborted), registration: options with { PendingRetrySeconds = 1, RenewJitterPercent = 0 }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        await WaitReadyAsync(upstream).ConfigureAwait(true);
        var assignedUrls = upstream.RegistrationState.Status!.AssignedUrls;
        DevelopmentCardinality.RequireSingle(assignedUrls);
        Assert.Equal(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"https://edge.site.test:{gateway.HttpsPort}/api/"), assignedUrls[0]);
        using var business = new DevelopmentSiteClient(options.Site.RootCertificate, gateway.HttpsPort, "edge.site.test");
        using var response = await business.Client.GetAsync(new Uri("/api/echo", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("initialized-service", await response.Content.ReadAsStringAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task SdkShutdownCannotRetainAReadyObservationWhenTheControllerIsUnavailableAsync()
    {
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var site = new DevelopmentInitializedSite(dns.Port);
        await using var siteLifetime = site.ConfigureAwait(true);
        site.Options["registration"]!["leaseSeconds"] = 90;
        site.Options["registration"]!["renewAfterSeconds"] = 30;
        Assert.Equal(0, await site.InitializeAsync().ConfigureAwait(true));
        await site.StartAsync().ConfigureAwait(true);
        var options = DravaRegistrationOptions.FromEnrollment(await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(site.EnrollmentPath, CancellationToken.None).ConfigureAwait(true), "svc-api", "/ready");
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("initialized-service", context.RequestAborted), registration: options with { PendingRetrySeconds = 1, RenewJitterPercent = 0 }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        await WaitReadyAsync(upstream).ConfigureAwait(true);
        var state = upstream.RegistrationState;
        await site.StopApplicationAsync().ConfigureAwait(true);
        await upstream.StopAsync().ConfigureAwait(true);
        Assert.Null(state.Status);
        Assert.NotEmpty(state.Failure);
    }

    private static async Task WaitReadyAsync(DevelopmentHttpUpstream upstream)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (upstream.RegistrationState.Status?.Phase != RegistrationPhase.Ready)
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
    }

    private static async Task AssertServedAsync(HttpClient client)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (true)
        {
            using var response = await client.GetAsync(new Uri("/echo", UriKind.Relative), deadline.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal(HttpVersion.Version20, response.Version);
                Assert.Equal("initialized-service", await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false));
                return;
            }
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
    }

    private static async Task AssertInitialGrantAsync(DevelopmentInitializedSite site, GatewayBootstrap gateway, NodeEnrollmentProfile profile)
    {
        using var client = new DevelopmentSiteClient(profile.Site.RootCertificate, gateway.ManagementPort, "admin.site.test", profile.Site.NodeCertificate);
        client.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await PortableConfiguration.LoadDocumentAsync<ApplicationBootstrap>(site.ApplicationPath, CancellationToken.None).ConfigureAwait(false)).AdministratorToken.RequireToken());
        using var response = await client.Client.GetAsync(new Uri("/admin/drava/registry?kind=nodes", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<RegistryStateResponse>().ConfigureAwait(false);
        Assert.NotNull(state);
        using var certificate = PortableCertificate.LoadPrivate(profile.Site.NodeCertificate);
        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        DevelopmentCardinality.RequireSingle(state.Nodes);
        var grant = state.Nodes[0];
        Assert.Equal("local", grant.NodeId); Assert.Equal("development", grant.OwnerId); Assert.Equal("svc", grant.ServicePrefix);
        Assert.Equal(fingerprint, grant.CertificateFingerprint);
        Assert.False(grant.Revoked); DevelopmentCardinality.RequireSingle(grant.EndpointAddresses); Assert.Equal("127.0.0.1", grant.EndpointAddresses[0], StringComparer.Ordinal);
    }

    private static void AssertPrivateFiles(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).Prepend(path))
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            if (!file.EndsWith(".lock", StringComparison.Ordinal)) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }
}
