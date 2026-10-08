using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Configuration;
using Mk8.Drava.Gateway.Hosting;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class PendingGatewayProcessTests
{
    [Fact]
    public async Task CachedPendingGatewayServesPrivateTlsAndClosesBusinessTlsWithoutApplicationAsync()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var application = PendingServingPlanTests.PublicApplication(fixture, publicMaterial);
        using var plans = await ServingPlanState.OpenAsync(application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var directory = fixture.Application.StateDirectory;
        var token = Path.Combine(directory, "gateway.token"); await File.WriteAllTextAsync(token, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).ConfigureAwait(true);
        File.SetUnixFileMode(token, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var root = fixture.Authority.PublicCertificate; var rootPath = Path.Combine(directory, "private-root.der"); await File.WriteAllBytesAsync(rootPath, root.RawData).ConfigureAwait(true);
        using var node = fixture.Authority.IssueNode("node", ["127.0.0.1"]); var nodePath = Path.Combine(directory, "node.pfx");
        await PrivateCertificateFile.WriteNewAsync(nodePath, node.Export(X509ContentType.Pkcs12), CancellationToken.None).ConfigureAwait(true);
        var gateway = fixture.Gateway with { HttpPort = TwoProcessProxy.UnusedPort(), HttpsPort = TwoProcessProxy.UnusedPort(), RegistrationPort = TwoProcessProxy.UnusedPort(), DiscoveryEnabled = false,
            ServingTrust = application.Controller!.ServingTrust, Application = new IpcEndpoint { UnixSocketPath = Path.Combine(directory, "absent.sock"), IdentityTokenPath = token } };
        var plan = plans.Read("local");
        foreach (var listener in plan.Listeners)
            listener.Port = checked((uint)(listener.Id switch { "http" => gateway.HttpPort, "https" => gateway.HttpsPort, "registration" => gateway.RegistrationPort, _ => throw new InvalidOperationException("Unexpected listener.") }));
        plan.ContentSha256 = Google.Protobuf.ByteString.CopyFrom(Mk8.Drava.Transport.Protocol.PresentationPlanDigest.Compute(plan));
        using (var cache = new GatewayPlanCache(directory)) await cache.WriteAsync(plan, CancellationToken.None).ConfigureAwait(true);
        var bootstrapPath = Path.Combine(directory, "pending-gateway.json"); await File.WriteAllTextAsync(bootstrapPath, BootstrapFile.Serialize(gateway)).ConfigureAwait(true);
        var process = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Gateway"), bootstrapPath);
        await using var processLifetime = process.ConfigureAwait(true);
        using var privateClient = new DevelopmentSiteClient(rootPath, gateway.RegistrationPort, "register.site.test", nodePath);
        await WaitForPrivateTlsAsync(process, privateClient.Client).ConfigureAwait(true);
        using var businessClient = new DevelopmentSiteClient(rootPath, gateway.HttpsPort, "svc.site.test");
        await Assert.ThrowsAsync<HttpRequestException>(async () => { using var response = await businessClient.Client.GetAsync(new Uri("/_drava/live", UriKind.Relative)).ConfigureAwait(false); }).ConfigureAwait(true);
        using var healthy = await privateClient.Client.GetAsync(new Uri("/_drava/live", UriKind.Relative)).ConfigureAwait(true); Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        Assert.Null(plans.ReadPublicationProof()); process.ThrowIfExited();
    }

    private static async Task WaitForPrivateTlsAsync(DevelopmentProcess process, HttpClient client)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            process.ThrowIfExited();
            try
            {
                using var response = await client.GetAsync(new Uri("/_drava/live", UriKind.Relative), deadline.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
    }
}
