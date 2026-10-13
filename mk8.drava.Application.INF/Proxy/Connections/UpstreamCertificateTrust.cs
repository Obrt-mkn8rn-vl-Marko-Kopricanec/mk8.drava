using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.INF.Proxy.Connections;

// This owner lives as long as the TLS transport. Framework policy clones retain
// references to the certificate objects in CustomTrustStore.
internal sealed class UpstreamCertificateTrust : IDisposable
{
    private readonly X509Certificate2? _root;

    private UpstreamCertificateTrust(X509Certificate2? root) => _root = root;

    public static UpstreamCertificateTrust Load(UpstreamTransportEndpoint endpoint)
    {
        var configured = endpoint.TrustedRoot;
        if (configured is null) return new UpstreamCertificateTrust(root: null);
        if (!endpoint.ValidateCertificate || !string.Equals(endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A trusted upstream root requires verified HTTPS.");

        var bytes = configured.CopyCertificate();
        X509Certificate2? certificate = null;
        try
        {
            certificate = X509CertificateLoader.LoadCertificate(bytes);
            if (!string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), configured.Sha256, StringComparison.Ordinal) || certificate.HasPrivateKey)
                throw new InvalidDataException("The upstream trust material must match its pinned public CA certificate.");
            RequireAuthority(certificate);
            var owner = new UpstreamCertificateTrust(certificate);
            certificate = null;
            return owner;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); certificate?.Dispose(); }
    }

    private static void RequireAuthority(X509Certificate2 certificate)
    {
        X509BasicConstraintsExtension? constraints = null;
        X509KeyUsageExtension? usage = null;
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is X509BasicConstraintsExtension basic)
            {
                if (constraints is not null) throw new InvalidDataException("Duplicate CA constraints.");
                constraints = basic;
            }
            if (extension is X509KeyUsageExtension keyUsage)
            {
                if (usage is not null) throw new InvalidDataException("Duplicate CA key usage.");
                usage = keyUsage;
            }
        }
        if (constraints is not { CertificateAuthority: true } || usage?.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign) != true)
        {
            throw new InvalidDataException("The upstream trust material must contain a public signing authority.");
        }
    }

    public SslClientAuthenticationOptions CreateOptions(UpstreamTransportEndpoint endpoint, List<SslApplicationProtocol>? applicationProtocols)
    {
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = endpoint.EffectiveSniHost,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            ApplicationProtocols = applicationProtocols,
        };
        // Verified profiles use the framework's complete validation and certificate cleanup.
        // Preserve the existing explicit insecure compatibility setting separately.
        if (!endpoint.ValidateCertificate)
            options.RemoteCertificateValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None || !endpoint.ValidateCertificate;
        if (_root is not null)
        {
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                VerificationFlags = X509VerificationFlags.NoFlag,
                RevocationMode = X509RevocationMode.NoCheck,
                DisableCertificateDownloads = true,
            };
            policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            policy.CustomTrustStore.Add(_root);
            options.CertificateChainPolicy = policy;
        }
        return options;
    }

    public void Dispose() => _root?.Dispose();
}
