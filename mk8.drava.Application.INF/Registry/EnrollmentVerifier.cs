using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Application.INF.Registry;

public sealed class EnrollmentVerifier : IDisposable
{
    private readonly X509Certificate2 _root;
    private readonly RegistryCoordinator _registry;
    private readonly TimeProvider _clock;

    public EnrollmentVerifier(X509Certificate2 trustRoot, RegistryCoordinator registry, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(trustRoot);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);
        if (UniqueExtension<X509BasicConstraintsExtension>(trustRoot)?.CertificateAuthority != true)
            throw new InvalidDataException("Enrollment trust must be an explicit private CA.");
        _root = X509CertificateLoader.LoadCertificate(trustRoot.RawData);
        _registry = registry;
        _clock = clock;
    }

    public AuthenticatedEnrollment Authenticate(ReadOnlySpan<byte> certificateDer)
    {
        if (certificateDer.Length is < 128 or > RegistrationProof.MaximumCertificateBytes) throw new UnauthorizedAccessException("Invalid enrollment certificate bounds.");
        using var certificate = X509CertificateLoader.LoadCertificate(certificateDer);
        if (UniqueExtension<X509BasicConstraintsExtension>(certificate)?.CertificateAuthority != false)
            throw new UnauthorizedAccessException("Enrollment requires a leaf certificate.");
        var usages = UniqueExtension<X509EnhancedKeyUsageExtension>(certificate);
        var clientAuthentication = false;
        if (usages is not null)
            foreach (Oid usage in usages.EnhancedKeyUsages)
                if (string.Equals(usage.Value, "1.3.6.1.5.5.7.3.2", StringComparison.Ordinal)) clientAuthentication = true;
        if (!clientAuthentication || (UniqueExtension<X509KeyUsageExtension>(certificate)?.KeyUsages & X509KeyUsageFlags.DigitalSignature) != X509KeyUsageFlags.DigitalSignature)
            throw new UnauthorizedAccessException("Enrollment requires explicit client authentication and signing usage.");
        using var key = certificate.GetECDsaPublicKey();
        if (key is null || !RegistrationProof.IsP256(key)) throw new UnauthorizedAccessException("Enrollment requires P-256.");
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(_root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationTime = _clock.GetUtcNow().UtcDateTime;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        if (!chain.Build(certificate)) throw new UnauthorizedAccessException("Enrollment chain or lifetime is invalid.");
        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var certificateNotAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
        foreach (var grant in _registry.State.Grants.Values)
            if (string.Equals(grant.CertificateFingerprint, fingerprint, StringComparison.Ordinal) && !grant.Revoked && _clock.GetUtcNow() < grant.NotAfterUtc)
            {
                if (grant.NotAfterUtc > certificateNotAfter) throw new UnauthorizedAccessException("Enrollment grant cannot outlive its credential.");
                return new AuthenticatedEnrollment(fingerprint, grant.NodeId, grant.OwnerId, certificateNotAfter);
            }
        throw new UnauthorizedAccessException("Unknown or revoked enrollment.");
    }

    public void Dispose() => _root.Dispose();

    private static T? UniqueExtension<T>(X509Certificate2 certificate) where T : X509Extension
    {
        T? found = null;
        foreach (var extension in certificate.Extensions)
            if (extension is T typed)
            {
                if (found is not null) throw new UnauthorizedAccessException("Duplicate enrollment certificate extension.");
                found = typed;
            }
        return found;
    }
}
