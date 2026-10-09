namespace Mk8.Drava.Configuration;

public sealed class GatewayRequestBodyLimit
{
    public string Host { get; init; } = "";
    public long MaxRequestBodyBytes { get; init; }

    public void Validate()
    {
        if (Host is null || Host.Length is < 3 or > 253 || !Host.Contains('.', StringComparison.Ordinal))
            throw new InvalidDataException("A Gateway body limit requires an exact canonical hostname.");
        foreach (var label in Host.Split('.')) RegistrationSiteTrust.RequireLabel(label);
        if (MaxRequestBodyBytes is < 0 or > 1L * 1024 * 1024 * 1024 * 1024)
            throw new InvalidDataException("A Gateway body limit must be between zero and one TiB.");
    }
}
