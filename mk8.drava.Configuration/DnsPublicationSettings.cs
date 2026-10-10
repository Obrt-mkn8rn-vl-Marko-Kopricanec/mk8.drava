namespace Mk8.Drava.Configuration;

public sealed record DnsPublicationSettings
{
    public string Provider { get; init; } = "existing";
    public Uri? ApiBaseUrl { get; init; }
    public string ZoneId { get; init; } = "";
    public string ZoneName { get; init; } = "";
    public string CredentialPath { get; init; } = "";
    public NativeDnsManagementSettings? NativeManagement { get; init; }
    public int TtlSeconds { get; init; } = 300;
    public int RequestTimeoutSeconds { get; init; } = 5;
    public int RetrySeconds { get; init; } = 30;

    public void Validate(string siteDomain)
    {
        ArgumentNullException.ThrowIfNull(siteDomain);
        RequireDomain(siteDomain);
        if (TtlSeconds is < 60 or > 86400 || RequestTimeoutSeconds is < 1 or > 30 || RetrySeconds is < 5 or > 3600)
            throw new InvalidDataException("DNS publication timing is outside supported bounds.");
        if (string.Equals(Provider, "mk8.dns", StringComparison.Ordinal))
        {
            if (NativeManagement is null || ApiBaseUrl is not null || ZoneId.Length != 0 || ZoneName.Length != 0 || CredentialPath.Length != 0 || RequestTimeoutSeconds < 20)
                throw new InvalidDataException("Native DNS requires its explicit separate management profile and at least20second overall request bound.");
            NativeManagement.Validate(siteDomain, acme: false);
            return;
        }
        if (NativeManagement is not null) throw new InvalidDataException("Native DNS management settings cannot accompany another provider.");
        if (string.Equals(Provider, "existing", StringComparison.Ordinal))
        {
            if (ApiBaseUrl is not null || ZoneId.Length != 0 || ZoneName.Length != 0 || CredentialPath.Length != 0)
                throw new InvalidDataException("Existing DNS verification does not accept publisher credentials or zone settings.");
            return;
        }
        if (!string.Equals(Provider, "cloudflare", StringComparison.Ordinal) || ZoneId.Length != 32 || !Path.IsPathFullyQualified(CredentialPath))
            throw new InvalidDataException("DNS publication requires a supported provider, zone identity and private credential path.");
        _ = RequireApiBaseUrl();
        foreach (var character in ZoneId)
            if (character is not (>= 'a' and <= 'f') and not (>= '0' and <= '9'))
                throw new InvalidDataException("DNS zone identity requires canonical hexadecimal.");
        RequireDomain(ZoneName);
        if (!string.Equals(siteDomain, ZoneName, StringComparison.Ordinal) && !siteDomain.EndsWith("." + ZoneName, StringComparison.Ordinal))
            throw new InvalidDataException("The DNS zone must contain the enrolled site domain.");
    }

    public Uri RequireApiBaseUrl()
    {
        var endpoint = ApiBaseUrl;
        if (endpoint is null || !endpoint.IsAbsoluteUri || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            endpoint.Host.Length == 0 || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            !endpoint.AbsolutePath.EndsWith('/'))
        {
            throw new InvalidDataException("DNS publication requires an explicitly configured HTTPS API base URL ending in a slash, without credentials, query or fragment.");
        }
        return endpoint;
    }

    private static void RequireDomain(string domain)
    {
        if (domain.Length is < 3 or > 253 || !domain.Contains('.', StringComparison.Ordinal))
            throw new InvalidDataException("DNS publication requires a canonical fully qualified domain.");
        foreach (var label in domain.Split('.')) RegistrationSiteTrust.RequireLabel(label);
    }
}
