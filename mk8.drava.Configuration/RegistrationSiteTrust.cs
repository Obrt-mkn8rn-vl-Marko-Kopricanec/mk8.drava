namespace Mk8.Drava.Configuration;

public sealed record RegistrationSiteTrust
{
    public string SiteId { get; init; } = "";
    public string Domain { get; init; } = "";
    public string RootFingerprint { get; init; } = "";
    public string RootCertificatePath { get; init; } = "";
    public string NodeCertificatePath { get; init; } = "";

    public void Validate()
    {
        if (SiteId.Length is < 1 or > 63 || Domain.Length is < 3 or > 189 || !Domain.Contains('.', StringComparison.Ordinal)) throw new InvalidDataException("Invalid enrollment site identity or domain.");
        RequireLabel(SiteId);
        foreach (var label in Domain.Split('.')) RequireLabel(label);
        if (!Path.IsPathFullyQualified(RootCertificatePath) || !Path.IsPathFullyQualified(NodeCertificatePath)) throw new InvalidDataException("Enrollment files require absolute paths.");
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
