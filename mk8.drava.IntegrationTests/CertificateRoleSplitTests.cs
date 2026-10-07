using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.Hosting;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class CertificateRoleSplitTests
{
    [Fact]
    public async Task NewPlanContainsSeparateServingAndPrivateListenerKeysAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var plan = state.Read("local");
        Assert.Equal(2, plan.Certificates.Count);
        Assert.Equal("site", plan.Certificates[0].CertificateId);
        Assert.Equal("enrollment", plan.Certificates[1].CertificateId);
        using var serving = X509CertificateLoader.LoadPkcs12(plan.Certificates[0].Pfx.Span, password: null, X509KeyStorageFlags.EphemeralKeySet);
        using var enrollment = X509CertificateLoader.LoadPkcs12(plan.Certificates[1].Pfx.Span, password: null, X509KeyStorageFlags.EphemeralKeySet);
        Assert.NotEqual(serving.GetCertHashString(HashAlgorithmName.SHA256), enrollment.GetCertHashString(HashAlgorithmName.SHA256), StringComparer.Ordinal);
        Assert.NotEqual(serving.PublicKey.ExportSubjectPublicKeyInfo(), enrollment.PublicKey.ExportSubjectPublicKeyInfo());
        Assert.True(enrollment.MatchesHostname("register.site.test", allowWildcards: false, allowCommonName: false));
        Assert.True(enrollment.MatchesHostname("admin.site.test", allowWildcards: false, allowCommonName: false));
        Assert.False(enrollment.MatchesHostname("svc.site.test", allowWildcards: true, allowCommonName: false));
    }

    [Fact]
    public async Task ActualGatewayChoosesCertificateByListenerRoleAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("unused")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, administration: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var serving = await ReadPresentedFingerprintAsync(proxy, proxy.TlsPort, "svc.site.test", enrollment: false).ConfigureAwait(true);
        var registration = await ReadPresentedFingerprintAsync(proxy, proxy.RegistrationPort, "register.site.test", enrollment: true).ConfigureAwait(true);
        var management = await ReadPresentedFingerprintAsync(proxy, proxy.ManagementPort, "admin.site.test", enrollment: true).ConfigureAwait(true);
        Assert.NotEqual(serving, registration, StringComparer.Ordinal);
        Assert.Equal(registration, management);
    }

    [Fact]
    public async Task LegacySingleCertificatePlanMigratesWithAFreshGenerationAndAcknowledgmentAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var initial = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var legacy = initial.Read("local");
        legacy.Certificates.RemoveAt(1);
        legacy.ContentSha256 = Google.Protobuf.ByteString.CopyFrom(Mk8.Drava.Transport.Protocol.PresentationPlanDigest.Compute(legacy));
        using (var recovered = new Mk8.Drava.Transport.Certificates.ValidatedServingPlan(legacy, fixture.Gateway, fixture.Clock, requireCurrent: true))
            Assert.Same(recovered.ServingCertificate, recovered.EnrollmentCertificate);
        await Mk8.Drava.Application.DAL.Publication.GatewayMaterialStore.WriteAsync(fixture.Application.StateDirectory, Google.Protobuf.MessageExtensions.ToByteArray(legacy), CancellationToken.None).ConfigureAwait(true);
        using var migrated = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var replacement = migrated.Read("local");
        Assert.Equal(legacy.Generation + 1, replacement.Generation);
        Assert.Equal(2, replacement.Certificates.Count);
        Assert.False(migrated.IsAcknowledged);
        Assert.Throws<InvalidDataException>(() => migrated.Acknowledge(Acknowledgment(legacy)));
        Assert.True(migrated.Acknowledge(Acknowledgment(replacement)));
    }

    [Theory]
    [InlineData("reversed")]
    [InlineData("duplicate-id")]
    [InlineData("missing-name")]
    [InlineData("changed-expiry")]
    [InlineData("global-expiry")]
    [InlineData("shared-key")]
    [InlineData("business-name")]
    [InlineData("foreign-ip")]
    public async Task RecomputedDigestCannotAuthorizeInvalidPrivateMaterialAsync(string corruption)
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var plan = state.Read("local");
        switch (corruption)
        {
            case "reversed":
                (plan.Certificates[0], plan.Certificates[1]) = (plan.Certificates[1], plan.Certificates[0]);
                break;
            case "duplicate-id": plan.Certificates[1].CertificateId = "site"; break;
            case "missing-name": plan.Certificates[1].HostNames.RemoveAt(1); break;
            case "changed-expiry": plan.Certificates[1].NotAfterUnixSeconds++; break;
            case "global-expiry": plan.ValidUntilUnixSeconds++; break;
            case "shared-key":
            case "business-name":
            case "foreign-ip":
                using (var serving = X509CertificateLoader.LoadPkcs12(plan.Certificates[0].Pfx.Span, password: null, X509KeyStorageFlags.EphemeralKeySet))
                using (var issuer = X509CertificateLoader.LoadPkcs12FromFile(fixture.Application.Controller!.CertificateAuthorityPath, password: null, X509KeyStorageFlags.EphemeralKeySet))
                using (var key = string.Equals(corruption, "shared-key", StringComparison.Ordinal) ? serving.GetECDsaPrivateKey()! : ECDsa.Create(ECCurve.NamedCurves.nistP256))
                using (var certificate = CreateInvalidPrivateLeaf(issuer, key, fixture.Clock.GetUtcNow(), corruption))
                {
                    plan.Certificates[1].Pfx = Google.Protobuf.ByteString.CopyFrom(certificate.Export(X509ContentType.Pkcs12));
                    plan.Certificates[1].NotAfterUnixSeconds = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()).ToUnixTimeSeconds();
                }
                break;
            default: throw new InvalidOperationException("Unknown fixture corruption.");
        }
        plan.ContentSha256 = Google.Protobuf.ByteString.CopyFrom(Mk8.Drava.Transport.Protocol.PresentationPlanDigest.Compute(plan));
        Assert.Throws<InvalidDataException>(() => new Mk8.Drava.Transport.Certificates.ValidatedServingPlan(plan, fixture.Gateway, fixture.Clock, requireCurrent: true));
    }

    private static X509Certificate2 CreateInvalidPrivateLeaf(X509Certificate2 issuer, ECDsa key, DateTimeOffset now, string corruption)
    {
        var request = new CertificateRequest("CN=development-private", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("register.site.test"); names.AddDnsName("admin.site.test");
        if (string.Equals(corruption, "business-name", StringComparison.Ordinal)) names.AddDnsName("other.site.test");
        names.AddIpAddress(IPAddress.Parse(string.Equals(corruption, "foreign-ip", StringComparison.Ordinal) ? "127.0.0.2" : "127.0.0.1"));
        request.CertificateExtensions.Add(names.Build());
        using var leaf = request.Create(issuer, now.AddMinutes(-5), now.AddDays(30), RandomNumberGenerator.GetBytes(16));
        return leaf.CopyWithPrivateKey(key);
    }

    private static Mk8.Drava.Transport.Protocol.V1.PlanAcknowledgment Acknowledgment(Mk8.Drava.Transport.Protocol.V1.PresentationPlan plan) => new()
    {
        Version = 1, GatewayId = plan.GatewayId, Generation = plan.Generation, Applied = true, ContentSha256 = plan.ContentSha256,
    };

    private static async Task<string> ReadPresentedFingerprintAsync(TwoProcessProxy proxy, int port, string host, bool enrollment)
    {
        using var validator = new DevelopmentSiteClient(proxy.RootCertificatePath, port, host);
        using var node = X509CertificateLoader.LoadPkcs12FromFile(proxy.NodeCertificatePath, password: null, X509KeyStorageFlags.EphemeralKeySet);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(true);
        string? fingerprint = null;
        using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (sender, certificate, chain, errors) =>
        {
            if (!validator.ValidateServer(sender, certificate, chain, errors)) return false;
            using var leaf = X509CertificateLoader.LoadCertificate(certificate!.GetRawCertData());
            fingerprint = leaf.GetCertHashString(HashAlgorithmName.SHA256);
            return true;
        });
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            ClientCertificates = enrollment ? new X509CertificateCollection { node } : null,
        }, timeout.Token).ConfigureAwait(true);
        Assert.False(string.IsNullOrEmpty(fingerprint));
        return fingerprint!;
    }
}
