namespace Mk8.Drava.Configuration;

public sealed class UpstreamTrustMaterial
{
    public string Id { get; init; } = "";
    public SecretBytes Certificate { get; init; } = SecretBytes.Empty;

    public void Validate()
    {
        if (Id is null || Id.Length is < 1 or > 128 || Id.Any(static character => character is not
            (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_' and not '.' and not '-'))
        {
            throw new InvalidDataException("Upstream trust material requires a bounded identifier.");
        }
        if (Certificate is null || Certificate.Length is < 1 or > 65536)
        {
            throw new InvalidDataException("Upstream trust material requires a bounded public certificate.");
        }
    }
}
