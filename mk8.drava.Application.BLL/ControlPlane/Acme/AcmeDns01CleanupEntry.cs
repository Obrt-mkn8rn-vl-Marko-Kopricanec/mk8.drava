using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;

public sealed record AcmeDns01CleanupEntry
{
    public AcmeDns01CleanupEntry(string zoneId, string siteDomain, string siteId, string operationId, string host, string value, string recordId)
    {
        ArgumentNullException.ThrowIfNull(zoneId); ArgumentNullException.ThrowIfNull(siteDomain); ArgumentNullException.ThrowIfNull(siteId);
        ArgumentNullException.ThrowIfNull(operationId); ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(recordId);
        RequireHex(zoneId, allowEmpty: false); RequireHex(recordId, allowEmpty: true);
        RegistryNames.RequireLabel(siteId);
        RequireDomain(siteDomain);
        if (!Guid.TryParseExact(operationId, "N", out var operation) || !string.Equals(operationId, operation.ToString("N"), StringComparison.Ordinal))
            throw new InvalidDataException("Cleanup operation identity is not canonical.");
        const string prefix = "_acme-challenge.";
        if (!host.StartsWith(prefix, StringComparison.Ordinal) || host.Length > 253)
            throw new InvalidDataException("Cleanup requires a bounded DNS01 owner name.");
        var domain = host[prefix.Length..]; RequireDomain(domain);
        if (!string.Equals(domain, siteDomain, StringComparison.Ordinal) && !domain.EndsWith("." + siteDomain, StringComparison.Ordinal))
            throw new InvalidDataException("Cleanup owner is outside its enrolled site.");
        if (value.Length != 43) throw new InvalidDataException("Cleanup requires a SHA-256 base64url digest.");
        foreach (var character in value)
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_') throw new InvalidDataException("Cleanup digest is not canonical.");
        ZoneId = zoneId; SiteDomain = siteDomain; SiteId = siteId; OperationId = operationId;
        Host = host; Value = value; RecordId = recordId;
    }

    public string ZoneId { get; }
    public string SiteDomain { get; }
    public string SiteId { get; }
    public string OperationId { get; }
    public string Host { get; }
    public string Value { get; }
    public string RecordId { get; }

    private static void RequireDomain(string domain)
    {
        if (domain.Length is < 3 or > 253 || !domain.Contains('.', StringComparison.Ordinal)) throw new InvalidDataException("Cleanup requires a canonical domain.");
        foreach (var label in domain.Split('.')) RegistryNames.RequireLabel(label);
    }

    private static void RequireHex(string value, bool allowEmpty)
    {
        if (allowEmpty && value.Length == 0) return;
        if (value.Length != 32) throw new InvalidDataException("Cleanup provider identity is invalid.");
        foreach (var character in value)
            if (character is not (>= 'a' and <= 'f') and not (>= '0' and <= '9')) throw new InvalidDataException("Cleanup provider identity is not canonical.");
    }
}
