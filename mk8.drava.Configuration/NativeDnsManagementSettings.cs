namespace Mk8.Drava.Configuration;

public sealed record NativeDnsManagementSettings
{
    public string SocketPath { get; init; } = "";
    public Guid TenantId { get; init; }
    public Guid ZoneId { get; init; }
    public string Origin { get; init; } = "";
    public string CredentialPath { get; init; } = "";
    public IReadOnlyList<NativeDnsOwnerScope> Scopes { get; init; } = [];

    public void Validate(string siteDomain, bool acme)
    {
        RequireDomain(Origin); RequireDomain(siteDomain);
        if (TenantId == Guid.Empty || ZoneId == Guid.Empty || !Path.IsPathFullyQualified(SocketPath) ||
            !string.Equals(Path.GetFullPath(SocketPath), SocketPath, StringComparison.Ordinal) || SocketPath.Contains('\0', StringComparison.Ordinal) ||
            System.Text.Encoding.UTF8.GetByteCount(SocketPath) > 100 || !Path.IsPathFullyQualified(CredentialPath) ||
            !string.Equals(Path.GetFullPath(CredentialPath), CredentialPath, StringComparison.Ordinal))
            throw new InvalidDataException("Native DNS requires concrete tenant/zone identities, a canonical local Unix socket and a separate private capability file.");
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

    internal static void RequireDomain(string value)
    {
        if (value is null || value.Length is < 3 or > 253 || !value.Contains('.', StringComparison.Ordinal))
            throw new InvalidDataException("Native DNS requires a canonical fully qualified name.");
        foreach (var label in value.Split('.')) RegistrationSiteTrust.RequireLabel(label);
    }
}
