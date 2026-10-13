namespace Mk8.Drava.Configuration;

public sealed record NativeDnsManagementSettings
{
    public string SocketPath { get; init; } = "";
    public Guid TenantId { get; init; }
    public Guid ZoneId { get; init; }
    public string Origin { get; init; } = "";
    public Guid ProfileId { get; init; }
    public SecretText Credential { get; init; } = SecretText.Empty;
    public IReadOnlyList<NativeDnsOwnerScope> Scopes { get; init; } = [];

    public void Validate(string siteDomain, bool acme)
    {
        RequireDomain(Origin); RequireDomain(siteDomain);
        if (ProfileId == Guid.Empty || TenantId == Guid.Empty || ZoneId == Guid.Empty || !Path.IsPathFullyQualified(SocketPath) ||
            !string.Equals(Path.GetFullPath(SocketPath), SocketPath, StringComparison.Ordinal) || SocketPath.Contains('\0', StringComparison.Ordinal) ||
            System.Text.Encoding.UTF8.GetByteCount(SocketPath) > 100)
        {
            throw new InvalidDataException("Native DNS requires concrete tenant/zone identities, a canonical local Unix socket and an explicit profile identity and protected capability.");
        }

        var credential = DecodeCredential();
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(credential);
        if (!string.Equals(siteDomain, Origin, StringComparison.Ordinal) && !siteDomain.EndsWith("." + Origin, StringComparison.Ordinal))
            throw new InvalidDataException("Native DNS origin must contain the enrolled site.");
        if (Scopes is null || Scopes.Count is < 1 or > 64) throw new InvalidDataException("Native DNS requires at most64 explicit owner/type scopes.");
        var unique = new HashSet<(string, ushort)>();
        foreach (var scope in Scopes)
        {
            if (scope is null) throw new InvalidDataException("Native DNS owner scope is missing.");
            scope.Validate(Origin, acme);
            if (!unique.Add((scope.Owner, scope.Type))) throw new InvalidDataException("Native DNS owner/type scope is duplicated.");
        }
    }

    public byte[] DecodeCredential()
    {
        var value = Credential.RequireToken();
        if (value.Length != 43) throw new InvalidDataException("Native DNS requires a canonical32byte base64url capability.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "="); }
        catch (FormatException exception) { throw new InvalidDataException("Native DNS capability encoding is invalid.", exception); }
        if (bytes.Length == 32 && string.Equals(Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'), value, StringComparison.Ordinal)) return bytes;
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        throw new InvalidDataException("Native DNS requires a canonical32byte base64url capability.");
    }

    internal static void RequireDomain(string value)
    {
        if (value is null || value.Length is < 3 or > 253 || !value.Contains('.', StringComparison.Ordinal))
            throw new InvalidDataException("Native DNS requires a canonical fully qualified name.");
        foreach (var label in value.Split('.')) RegistrationSiteTrust.RequireLabel(label);
    }
}
