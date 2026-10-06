using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.NodeRelay;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Contracts.Relay.V1;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.Application.INF.NodeRelay;

public sealed class NodeRelayAuthorizer : IDisposable
{
    private readonly string _siteId;
    private readonly X509Certificate2 _root;
    private readonly NodeRelayMappings _mappings;
    private readonly TimeProvider _clock;

    public NodeRelayAuthorizer(string siteId, X509Certificate2 root, NodeRelayMappings mappings, TimeProvider clock)
    {
        RegistryNames.RequireLabel(siteId);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(clock);
        _siteId = siteId;
        _root = X509CertificateLoader.LoadCertificate(root.RawData);
        _mappings = mappings;
        _clock = clock;
    }

    public (RelayCapability Capability, InstanceIntent Endpoint) Authorize(X509Certificate2 mutualTlsPeer, ReadOnlyMemory<byte> payload, ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(mutualTlsPeer);
        var capability = RelayCapabilityJson.Decode(payload);
        if (!string.Equals(capability.Identity.SiteId, _siteId, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Relay capability belongs to another site.");
        ControllerCertificateRole.Validate(mutualTlsPeer, _root, _siteId, capability.ControllerEpoch, _clock);
        using var key = mutualTlsPeer.GetECDsaPublicKey() ?? throw new UnauthorizedAccessException("Controller key is missing.");
        if (!RelayCapabilityProof.Verify(key, _siteId, payload.Span, signature)) throw new UnauthorizedAccessException("Relay capability was not signed by the authenticated controller peer.");
        return (capability, _mappings.AuthorizeAndConsume(capability));
    }

    public void Dispose() => _root.Dispose();
}
