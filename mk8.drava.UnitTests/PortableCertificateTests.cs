using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortableCertificateTests
{
    [Fact]
    public void ValuesOwnTheirInputAndEveryRevealedCopy()
    {
        byte[] input = [1, 2, 3];
        var material = new SecretBytes(input);
        input[0] = 99;
        var revealed = material.Reveal(1, 3);
        revealed[1] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, material.Reveal(1, 3));
        var same = new SecretBytes([1, 2, 3]);
        Assert.Equal(same, material); Assert.Equal(same.GetHashCode(), material.GetHashCode());
        Assert.Equal("[redacted]", material.ToString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"AQI\"")]
    [InlineData("\"AR==\"")]
    [InlineData("\"AQ==\\n\"")]
    public void ProtectedBinaryEncodingRejectsNullMalformedAndNoncanonicalValues(string value)
    {
        var json = System.Text.Encoding.UTF8.GetBytes("{\"clientCertificate\":" + value + "}");
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<IpcEndpoint>(json));
    }

    [Fact]
    public async Task ProtectedCollectionCarriesEnrollmentWithoutRawCertificateFilesAsync()
    {
        using var directory = new RegistryStateDirectory();
        using var authority = LocalSiteCertificateAuthority.Create("site", TimeProvider.System);
        using var root = authority.PublicCertificate;
        using var node = authority.IssueNode("node", ["127.0.0.1"]);
        var configured = new RegistrationSiteTrust
        {
            SiteId = "site", Domain = "site.test", RootFingerprint = authority.Fingerprint,
            RootCertificate = new SecretBytes(root.RawData), NodeCertificate = PortableCertificate.ExportPrivate(node),
            NodeAgentDescriptorPath = Path.Combine(directory.Path, "agent.json"),
        };
        var path = Path.Combine(directory.Path, ".env");
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(configured), CancellationToken.None).ConfigureAwait(true);
        var loaded = await PortableConfiguration.LoadDocumentAsync<RegistrationSiteTrust>(path, CancellationToken.None).ConfigureAwait(true);
        loaded.Validate();
        using var reopened = PortableCertificate.LoadPrivate(loaded.NodeCertificate);
        Assert.True(reopened.HasPrivateKey);
        Assert.Equal(node.RawData, reopened.RawData);
        Assert.Equal(configured, loaded);
        var privateValue = Convert.ToBase64String(configured.NodeCertificate.Reveal(128, 65536));
        Assert.DoesNotContain(privateValue, loaded.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(privateValue, await File.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory.Path, "node.pfx")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "root.der")));
    }

    [Fact]
    public void LocalEndpointCannotPublishRemotePrivateCertificateMaterial()
    {
        var endpoint = new IpcEndpoint { NamedPipeName = "fixture", ClientCertificate = new SecretBytes([1, 2, 3]) };
        Assert.Throws<InvalidDataException>(endpoint.Validate);
    }

    [Fact]
    public void EnrollmentRejectsAPrivateKeyOutsideItsSigningProtocol()
    {
        using var authority = LocalSiteCertificateAuthority.Create("site", TimeProvider.System);
        using var root = authority.PublicCertificate;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest("CN=fixture", key, HashAlgorithmName.SHA256);
        using var node = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var trust = new RegistrationSiteTrust
        {
            SiteId = "site", Domain = "site.test", RootFingerprint = authority.Fingerprint,
            RootCertificate = new SecretBytes(root.RawData), NodeCertificate = PortableCertificate.ExportPrivate(node),
        };
        Assert.Throws<InvalidDataException>(trust.Validate);
    }

    [Fact]
    public void RetiredCertificateLocatorsAreRejected()
    {
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<RegistrationSiteTrust>("{\"rootCertificatePath\":\"/reserved/fixture\"}"u8.ToArray()));
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<RegistrationSiteTrust>("{\"nodeCertificatePath\":\"/reserved/fixture\"}"u8.ToArray()));
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<ControllerBootstrap>("{\"certificateAuthorityPath\":\"/reserved/fixture\"}"u8.ToArray()));
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<IpcEndpoint>("{\"clientCertificatePath\":\"/reserved/fixture\"}"u8.ToArray()));
    }
}
