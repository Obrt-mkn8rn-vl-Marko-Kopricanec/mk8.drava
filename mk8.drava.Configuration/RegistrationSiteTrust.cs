using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mk8.Drava.Configuration;

public sealed record RegistrationSiteTrust
{
    public string SiteId { get; init; } = "";
    public string Domain { get; init; } = "";
    public string RootFingerprint { get; init; } = "";
    public SecretBytes RootCertificate { get; init; } = SecretBytes.Empty;
    public SecretBytes NodeCertificate { get; init; } = SecretBytes.Empty;
    public string NodeAgentDescriptorPath { get; init; } = "";

    public void Validate()
    {
        ValidateIdentity();
        using var root = PortableCertificate.LoadPublic(RootCertificate);
        using var node = PortableCertificate.LoadPrivate(NodeCertificate);
        using var key = node.GetECDsaPrivateKey();
        if (key is null || key.KeySize != 256 || !string.Equals(key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value, "1.2.840.10045.3.1.7", StringComparison.Ordinal))
            throw new InvalidDataException("Node enrollment requires its configured private P256 signing key.");
        if (!string.Equals(root.GetCertHashString(HashAlgorithmName.SHA256), RootFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("Enrollment root pin does not match its protected certificate.");
    }

    internal void ValidateIdentity()
    {
        if (SiteId.Length is < 1 or > 63 || Domain.Length is < 3 or > 189 || !Domain.Contains('.', StringComparison.Ordinal)) throw new InvalidDataException("Invalid enrollment site identity or domain.");
        RequireLabel(SiteId);
        foreach (var label in Domain.Split('.')) RequireLabel(label);
        if (NodeAgentDescriptorPath.Length != 0 && (!Path.IsPathFullyQualified(NodeAgentDescriptorPath) ||
            !string.Equals(Path.GetFullPath(NodeAgentDescriptorPath), NodeAgentDescriptorPath, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Node-agent descriptor requires a canonical absolute state path.");
        }

        if (RootFingerprint.Length != 64) throw new InvalidDataException("Site trust requires an enrolled SHA-256 root fingerprint.");
        foreach (var character in RootFingerprint)
            if (character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')) throw new InvalidDataException("Invalid enrollment fingerprint.");
    }

    public static void RequireLabel(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > 63 || value.StartsWith('-') || value.EndsWith('-')) throw new InvalidDataException("Identity requires a canonical DNS label.");
        foreach (var character in value)
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-') throw new InvalidDataException("Identity requires a canonical DNS label.");
    }
}
