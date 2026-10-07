namespace Mk8.Drava.Configuration;

public sealed record ServingTrustSettings
{
    public string Mode { get; init; } = "site-ca";
    public string RootFingerprint { get; init; } = "";

    public void Validate()
    {
        if (Mode is not ("site-ca" or "system" or "pinned"))
            throw new InvalidDataException("Unrecognized serving certificate trust mode.");
        if (!string.Equals(Mode, "pinned", StringComparison.Ordinal))
        {
            if (RootFingerprint.Length != 0) throw new InvalidDataException("Only pinned serving trust accepts a root fingerprint.");
            return;
        }
        if (RootFingerprint.Length != 64) throw new InvalidDataException("Pinned serving trust requires a SHA-256 root fingerprint.");
        foreach (var character in RootFingerprint)
            if (character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))
                throw new InvalidDataException("Serving root fingerprint requires canonical hexadecimal encoding.");
    }
}
