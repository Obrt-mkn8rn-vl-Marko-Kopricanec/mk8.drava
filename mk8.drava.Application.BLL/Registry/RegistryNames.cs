namespace Mk8.Drava.Application.BLL.Registry;

public static class RegistryNames
{
    public static void RequireLabel(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > 63 || value[0] == '-' || value[^1] == '-') throw new InvalidDataException("Invalid registry label.");
        foreach (var character in value)
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
                throw new InvalidDataException("Registry labels use lowercase ASCII DNS labels.");
    }

    public static void RequireEpoch(string value)
    {
        if (!Guid.TryParseExact(value, "N", out var epoch) || epoch == Guid.Empty || !string.Equals(value, epoch.ToString("N"), StringComparison.Ordinal))
            throw new InvalidDataException("Registry epochs require nonempty canonical GUIDs.");
    }

    public static void RequireFingerprint(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64) throw new InvalidDataException("Invalid enrollment fingerprint.");
        foreach (var character in value)
            if (character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))
                throw new InvalidDataException("Invalid enrollment fingerprint encoding.");
    }
}
