using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Transport.Relay;

public static class NodeCertificateTrust
{
    public static bool Validate(X509Certificate2 certificate, X509Certificate2 root, string address, string fingerprint, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(clock);
        if (!IPAddress.TryParse(address, out _) || certificate.RawData.Length > RegistrationProof.MaximumCertificateBytes || certificate.Extensions.Count > 64 ||
            !string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), fingerprint, StringComparison.Ordinal) ||
            !certificate.MatchesHostname(address, allowWildcards: false, allowCommonName: false)) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var leaf = false;
        var signing = false;
        var server = false;
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value is not { } oid || !seen.Add(oid)) return false;
            if (extension is X509BasicConstraintsExtension basic) { if (basic.CertificateAuthority) return false; leaf = true; }
            if (extension is X509KeyUsageExtension usage) signing = (usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) != X509KeyUsageFlags.None;
            if (extension is X509EnhancedKeyUsageExtension enhanced)
                foreach (Oid value in enhanced.EnhancedKeyUsages)
                    if (string.Equals(value.Value, "1.3.6.1.5.5.7.3.1", StringComparison.Ordinal)) server = true;
        }
        if (!leaf || !signing || !server) return false;
        using var key = certificate.GetECDsaPublicKey();
        if (key is null || !RegistrationProof.IsP256(key)) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.VerificationTime = clock.GetUtcNow().UtcDateTime;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        return chain.Build(certificate);
    }
}
