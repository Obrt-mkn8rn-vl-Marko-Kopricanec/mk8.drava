using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Transport.Relay;

public static class ControllerCertificateRole
{
    public static Uri Identity(string siteId, string controllerEpoch)
    {
        RegistrationSiteTrust.RequireLabel(siteId);
        if (!Guid.TryParseExact(controllerEpoch, "N", out var epoch) || epoch == Guid.Empty ||
            !string.Equals(epoch.ToString("N"), controllerEpoch, StringComparison.Ordinal))
            throw new InvalidDataException("Controller epoch must be canonical.");
        return new Uri("urn:mk8.drava:controller:" + siteId + ":" + controllerEpoch, UriKind.Absolute);
    }

    public static void Validate(X509Certificate2 certificate, X509Certificate2 root, string siteId, string controllerEpoch, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(clock);
        RequireUniqueExtensions(certificate);
        RequireUniqueExtensions(root);
        if (Extension<X509BasicConstraintsExtension>(root)?.CertificateAuthority != true ||
            Extension<X509BasicConstraintsExtension>(certificate)?.CertificateAuthority != false ||
            (Extension<X509KeyUsageExtension>(certificate)?.KeyUsages & X509KeyUsageFlags.DigitalSignature) != X509KeyUsageFlags.DigitalSignature)
            throw new UnauthorizedAccessException("Relay controller requires an explicit signing leaf and private trust root.");
        RequireClientUsage(certificate);
        RequireRole(certificate, Identity(siteId, controllerEpoch).AbsoluteUri);
        using var key = certificate.GetECDsaPublicKey();
        if (key is null || !RegistrationProof.IsP256(key)) throw new UnauthorizedAccessException("Relay controller requires P-256.");
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.VerificationTime = clock.GetUtcNow().UtcDateTime;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        if (!chain.Build(certificate)) throw new UnauthorizedAccessException("Relay controller trust or lifetime is invalid.");
    }

    private static void RequireClientUsage(X509Certificate2 certificate)
    {
        var usages = Extension<X509EnhancedKeyUsageExtension>(certificate) ?? throw new UnauthorizedAccessException("Missing controller client authentication usage.");
        foreach (Oid usage in usages.EnhancedKeyUsages)
            if (string.Equals(usage.Value, "1.3.6.1.5.5.7.3.2", StringComparison.Ordinal)) return;
        throw new UnauthorizedAccessException("Missing controller client authentication usage.");
    }

    private static void RequireRole(X509Certificate2 certificate, string expected)
    {
        var san = certificate.Extensions["2.5.29.17"] ?? throw new UnauthorizedAccessException("Missing controller role.");
        try
        {
            var reader = new AsnReader(san.RawData, AsnEncodingRules.DER);
            var names = reader.ReadSequence();
            var value = names.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 6));
            names.ThrowIfNotEmpty();
            reader.ThrowIfNotEmpty();
            if (!string.Equals(value, expected, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Controller role does not match site and writer epoch.");
        }
        catch (AsnContentException exception) { throw new UnauthorizedAccessException("Invalid controller role encoding.", exception); }
    }

    private static void RequireUniqueExtensions(X509Certificate2 certificate)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in certificate.Extensions)
            if (extension.Oid?.Value is not { } oid || !seen.Add(oid)) throw new UnauthorizedAccessException("Duplicate or missing certificate extension identity.");
    }

    private static T? Extension<T>(X509Certificate2 certificate) where T : X509Extension
    {
        foreach (var extension in certificate.Extensions)
            if (extension is T typed) return typed;
        return null;
    }
}
