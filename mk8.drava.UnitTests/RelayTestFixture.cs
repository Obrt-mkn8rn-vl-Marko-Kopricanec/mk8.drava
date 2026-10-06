using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.NodeRelay;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.INF.NodeRelay;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Contracts.Relay.V1;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.UnitTests;

internal sealed class RelayTestFixture : IDisposable
{
    public RegistryTimeProvider Clock { get; } = new();
    public string ControllerEpoch { get; } = Guid.NewGuid().ToString("N");
    public string AgentBootId { get; } = Guid.NewGuid().ToString("N");
    public X509Certificate2 Root { get; }
    public X509Certificate2 Controller { get; }
    public NodeRelayMappings Mappings { get; }
    public NodeRelayAuthorizer Authorizer { get; }
    public RegistrationCommand Command { get; } = EnrollmentTestFixture.Command();

    public RelayTestFixture()
    {
        Root = EnrollmentTestFixture.CreateRoot(Clock);
        Controller = CreateController(Root, Clock, "site", ControllerEpoch);
        var grant = new NodeGrant("node", "owner", RegistryTestFixture.Fingerprint, "svc", ["127.0.0.1", "192.0.2.10"], 1024, 65535, Clock.GetUtcNow().AddDays(1), revoked: false);
        Mappings = new NodeRelayMappings("site", AgentBootId, grant, ["127.0.0.1"], Clock);
        Authorizer = new NodeRelayAuthorizer("site", Root, Mappings, Clock);
        Mappings.Renew(Command.Identity, Command.Advertisement!);
    }

    public RelayCapability Capability(RegistrationIdentity? identity = null, ServiceAdvertisement? advertisement = null) => new()
    {
        Identity = identity ?? Command.Identity, Advertisement = advertisement ?? Command.Advertisement!,
        AgentBootId = AgentBootId, ControllerEpoch = ControllerEpoch, ExchangeId = Guid.NewGuid().ToString("N"), CapabilityId = Guid.NewGuid().ToString("N"),
        Purpose = RelayPurpose.Exchange, IssuedAtUnixMilliseconds = Clock.GetUtcNow().ToUnixTimeMilliseconds(),
        ExpiresAtUnixMilliseconds = Clock.GetUtcNow().AddSeconds(10).ToUnixTimeMilliseconds(), MaximumBytesPerDirection = 1024 * 1024, MaximumDurationSeconds = 60,
    };

    public (RelayCapability Capability, InstanceIntent Endpoint) Authorize(RelayCapability capability, X509Certificate2? signer = null, X509Certificate2? peer = null)
    {
        signer ??= Controller;
        var payload = RelayCapabilityJson.Encode(capability);
        using var key = signer.GetECDsaPrivateKey() ?? throw new InvalidOperationException("Relay fixture key is missing.");
        return Authorizer.Authorize(peer ?? signer, payload, RelayCapabilityProof.Sign(key, capability.Identity.SiteId, payload));
    }

    public void Dispose()
    {
        Authorizer.Dispose();
        Controller.Dispose();
        Root.Dispose();
    }

    public static X509Certificate2 CreateController(X509Certificate2 root, TimeProvider clock, string siteId, string epoch)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=controller", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(ControllerCertificateRole.Identity(siteId, epoch));
        request.CertificateExtensions.Add(names.Build());
        using var leaf = request.Create(root, clock.GetUtcNow().AddMinutes(-1), clock.GetUtcNow().AddHours(1), RandomNumberGenerator.GetBytes(16));
        return leaf.CopyWithPrivateKey(key);
    }
}
