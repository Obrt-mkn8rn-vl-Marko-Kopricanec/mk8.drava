using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;

public sealed class PortableManualConfigurationSource : IProxyManualConfigurationSource
{
    public const string CollectionKey = "Drava:ManualSource:ApplicationBootstrap";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false, MaxDepth = 8,
    };
    private readonly PortableRole<ApplicationBootstrap> _role;
    private readonly RuntimeAdminSecurityOptions _administrator;

    public PortableManualConfigurationSource(PortableRole<ApplicationBootstrap> role)
    {
        ArgumentNullException.ThrowIfNull(role);
        _role = role;
        var bootstrap = role.Configuration;
        var urls = bootstrap.ManagementPort > 0
            ? new[] { "https://admin." + (bootstrap.Controller?.Domain ?? throw new InvalidDataException("Protected management owner is absent."))
                + ":" + bootstrap.ManagementPort.ToString(CultureInfo.InvariantCulture), } : [];
        var token = bootstrap.AdministratorToken.IsEmpty ? null : bootstrap.AdministratorToken.RequireToken();
        _administrator = new RuntimeAdminSecurityOptions(urls, RequireAuthentication: true, HasConfiguredToken: token is not null,
            token, "DISABLED", token is null ? "not-configured" : "portable-collection", RecentAuditCapacity: 1024);
    }

    public async ValueTask<ProxyManualConfigurationInput> ReadAsync(CancellationToken cancellationToken)
    {
        var document = await _role.RefreshManualConfigurationAsync(cancellationToken).ConfigureAwait(false);
        return Parse(document, _role.Configuration.SiteId, _administrator);
    }

    public static PortableManualConfigurationDocument Encode(string siteId, string operationalDocument,
        IReadOnlyList<ProxyManualSiteInput> sites, IReadOnlyList<ProxyManualCertificateInput> certificates)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteId);
        ArgumentNullException.ThrowIfNull(operationalDocument);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(certificates);
        var envelope = new Envelope { SiteId = siteId, OperationalDocument = operationalDocument };
        foreach (var site in sites)
            envelope.Sites.Add(new Site { Name = site.Name, Format = FormatName(site.Format), Document = site.Document });
        foreach (var certificate in certificates)
        {
            var bytes = certificate.CopyPfx();
            try { envelope.Certificates.Add(certificate.Id, Convert.ToBase64String(bytes)); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        return new PortableManualConfigurationDocument(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static ProxyManualConfigurationInput Parse(PortableManualConfigurationDocument document, string siteId, RuntimeAdminSecurityOptions administrator)
    {
        if (document.IsEmpty) return new ProxyManualConfigurationInput("{}", [], [], administrator);
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(document.Reveal(), JsonOptions) ?? throw new InvalidDataException("Protected manual input is absent.");
            if (envelope.SchemaVersion != 1 || !string.Equals(envelope.SiteId, siteId, StringComparison.Ordinal)
                || envelope.OperationalDocument is null || envelope.Sites is null || envelope.Certificates is null
                || envelope.Sites.Count > 128 || envelope.Certificates.Count > 64)
            {
                throw new InvalidDataException("Protected manual input schema, site or inventory is invalid.");
            }

            var sites = new List<ProxyManualSiteInput>(envelope.Sites.Count);
            for (var index = 0; index < envelope.Sites.Count; index++)
            {
                var site = envelope.Sites[index];
                if (site is null || site.Document is null) throw new InvalidDataException("Protected manual site input is absent.");
                sites.Add(new ProxyManualSiteInput(site.Name, ParseFormat(site.Format), site.Document));
            }
            var certificates = new List<ProxyManualCertificateInput>(envelope.Certificates.Count);
            foreach (var certificate in envelope.Certificates)
            {
                if (certificate.Value is null || certificate.Value.Length > 1398104) throw new InvalidDataException("Protected manual PFX encoding exceeds its bound.");
                var bytes = Convert.FromBase64String(certificate.Value);
                try
                {
                    if (!string.Equals(Convert.ToBase64String(bytes), certificate.Value, StringComparison.Ordinal))
                        throw new InvalidDataException("Protected manual PFX encoding is not canonical.");
                    certificates.Add(new ProxyManualCertificateInput(certificate.Key, bytes));
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            return new ProxyManualConfigurationInput(envelope.OperationalDocument, sites, certificates, administrator);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            throw new InvalidDataException("Protected manual input is malformed.", exception);
        }
    }

    internal static void ValidateDocument(PortableManualConfigurationDocument document, string siteId) =>
        _ = Parse(document, siteId, new RuntimeAdminSecurityOptions([], RequireAuthentication: true, HasConfiguredToken: false,
            Token: null, "DISABLED", "not-configured", RecentAuditCapacity: 1024));

    private static string FormatName(ProxyManualSiteFormat format) => format switch
    {
        ProxyManualSiteFormat.Json => "json", ProxyManualSiteFormat.Yaml => "yaml",
        _ => throw new InvalidDataException("Protected manual site format is unsupported."),
    };

    private static ProxyManualSiteFormat ParseFormat(string format) => format switch
    {
        "json" => ProxyManualSiteFormat.Json, "yaml" => ProxyManualSiteFormat.Yaml,
        _ => throw new InvalidDataException("Protected manual site format is unsupported."),
    };

    private sealed class Envelope
    {
        [JsonRequired]
        public int SchemaVersion { get; init; } = 1;
        [JsonRequired]
        public string SiteId { get; init; } = "";
        [JsonRequired]
        public string OperationalDocument { get; init; } = "{}";
        [JsonRequired]
        public List<Site> Sites { get; init; } = [];
        [JsonRequired]
        public Dictionary<string, string> Certificates { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Site
    {
        public string Name { get; init; } = "";
        public string Format { get; init; } = "";
        public string Document { get; init; } = "";
    }
}
