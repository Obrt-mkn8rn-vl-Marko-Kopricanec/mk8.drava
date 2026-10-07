using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.INF.Registry;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.UnitTests;

internal sealed class EnrollmentTestFixture : IDisposable
{
    public EnrollmentTestFixture(IGatewayPublicationSource? publication = null)
    {
        Root = CreateRoot(Clock);
        Leaf = CreateLeaf(Root, Clock, client: true);
        Availability = new DestinationAvailabilityStore(Clock);
        Registry = new RegistryCoordinator(new MemoryRegistryRepository(), Availability, Clock);
        Verifier = new EnrollmentVerifier(Root, Registry, Clock);
        Challenges = new EnrollmentChallenges(Clock);
        Handler = new SignedRegistrationHandler("site", Registry, Availability, Verifier, Challenges, Clock, publication);
    }

    public RegistryTimeProvider Clock { get; } = new();
    public X509Certificate2 Root { get; }
    public X509Certificate2 Leaf { get; }
    public RegistryCoordinator Registry { get; }
    public DestinationAvailabilityStore Availability { get; }
    public EnrollmentVerifier Verifier { get; }
    public EnrollmentChallenges Challenges { get; }
    public SignedRegistrationHandler Handler { get; }

    public async ValueTask InitializeAsync()
    {
        await Registry.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        await EnrollAsync(Leaf).ConfigureAwait(false);
    }

    public ValueTask EnrollAsync(X509Certificate2 certificate) => Registry.EnrollAsync(new NodeGrant("node", "owner",
        certificate.GetCertHashString(HashAlgorithmName.SHA256), "svc", ["127.0.0.1"], 1024, 65535, Clock.GetUtcNow().AddDays(1), false), "administrator", CancellationToken.None);

    public static RegistrationCommand Command() => new()
    {
        Operation = RegistrationOperation.Register,
        Identity = new RegistrationIdentity { SiteId = "site", NodeId = "node", OwnerId = "owner", ServiceId = "svc", ContractId = "v1", InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N") },
        Advertisement = new ServiceAdvertisement { DeploymentId = "deployment", Address = "127.0.0.1", Port = 12345, ReadinessPath = "/ready" },
    };

    public SignedCommand Sign(RegistrationCommand command, string site = "site")
    {
        var challenge = Handler.Challenge(new ChallengeRequest { Version = 1, EnrollmentCertificateDer = ByteString.CopyFrom(Leaf.RawData) });
        var payload = RegistrationJson.Encode(command);
        using var key = Leaf.GetECDsaPrivateKey() ?? throw new InvalidOperationException("Fixture key is missing.");
        return new SignedCommand
        {
            Version = 1, EnrollmentCertificateDer = ByteString.CopyFrom(Leaf.RawData), Nonce = challenge.Nonce, JsonPayload = ByteString.CopyFrom(payload),
            Signature = ByteString.CopyFrom(RegistrationProof.Sign(key, site, 1, challenge.Nonce.Span, payload)),
        };
    }

    public void Dispose()
    {
        Verifier.Dispose();
        Registry.Dispose();
        Leaf.Dispose();
        Root.Dispose();
    }

    public static X509Certificate2 CreateRoot(TimeProvider clock)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=mk8.drava test site", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(clock.GetUtcNow().AddDays(-1), clock.GetUtcNow().AddYears(1));
    }

    public static X509Certificate2 CreateLeaf(X509Certificate2 issuer, TimeProvider clock, bool client)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=node", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, true));
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] |= 1;
        using var publicCertificate = request.Create(issuer, clock.GetUtcNow().AddHours(-1), clock.GetUtcNow().AddDays(1), serial);
        return publicCertificate.CopyWithPrivateKey(key);
    }
}
