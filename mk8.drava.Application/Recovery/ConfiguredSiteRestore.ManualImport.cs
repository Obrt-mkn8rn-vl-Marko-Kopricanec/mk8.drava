using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.DAL.Installation;
using Mk8.Drava.Application.DAL.Storage;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Recovery;

internal static partial class ConfiguredSiteRestore
{
    private const string LegacyOperational = "application/config/proxy.json";
    private const string LegacySites = "application/config/sites/";
    private static readonly UTF8Encoding StrictManualUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Inventory comes from the cloned caller approvals; never discover configuration in an installed directory.
    private static PortableManualConfigurationDocument? ImportApprovedManual(PrivateSiteWorkspace workspace, ConfiguredSiteRestoreRequest request)
    {
        var selected = request.Placements.Where(static placement => string.Equals(placement.RelativePath, LegacyOperational, StringComparison.Ordinal)
            || placement.RelativePath.StartsWith(LegacySites, StringComparison.Ordinal)).ToArray();
        if (selected.Length == 0) return null;
        var sites = new List<ProxyManualSiteInput>();
        var operational = "{}";
        foreach (var placement in selected)
        {
            if (string.Equals(placement.RelativePath, LegacyOperational, StringComparison.Ordinal))
            {
                operational = ReadApprovedManualText(Path.Combine(workspace.StagingDirectory, placement.RelativePath));
                continue;
            }
            var name = placement.RelativePath[LegacySites.Length..];
            if (name.Contains('/', StringComparison.Ordinal) || SiteConfigurationPlaceholderFiles.IsPlaceholderFileName(name)) continue;
            if (!SiteConfigurationFileDiscovery.TryParseFormat(Path.GetExtension(name).TrimStart('.'), out var format)) continue;
            sites.Add(new ProxyManualSiteInput(name, format == SiteConfigurationFormat.Json ? ProxyManualSiteFormat.Json : ProxyManualSiteFormat.Yaml,
                ReadApprovedManualText(Path.Combine(workspace.StagingDirectory, placement.RelativePath))));
        }
        var options = JsonSerializer.Deserialize<ProxyOperationalOptions>(operational, SiteConfigurationParser.ReadJsonOptions)
            ?? throw new InvalidDataException("Approved operational configuration is absent.");
        if (options.Admin is null || options.Certificates is null) throw new InvalidDataException("Approved operational sections are absent.");
        var certificates = ImportApprovedCertificates(workspace, request, options.Certificates);
        var projected = new ProxyOperationalOptions
        {
            Admin = new ProxyAdminOptions { Urls = options.Admin.Urls, RequireAuthentication = options.Admin.RequireAuthentication,
                RecentAuditCapacity = options.Admin.RecentAuditCapacity, TokenEnvironmentVariable = "DISABLED", },
            Acme = options.Acme, Metrics = options.Metrics, Timeouts = options.Timeouts, Connections = options.Connections,
            Observability = options.Observability, Limits = options.Limits, ForwardedHeaders = options.ForwardedHeaders,
            Certificates = [.. options.Certificates.Select(static certificate => new CertificateOptions
            { Id = certificate.Id, Format = certificate.Format, Password = certificate.Password })],
        };
        return PortableManualConfigurationSource.Encode(request.Application.SiteId, JsonSerializer.Serialize(projected, SiteConfigurationParser.ReadJsonOptions), sites, certificates);
    }

    private static List<ProxyManualCertificateInput> ImportApprovedCertificates(PrivateSiteWorkspace workspace,
        ConfiguredSiteRestoreRequest request, IList<CertificateOptions> options)
    {
        var certificates = new List<ProxyManualCertificateInput>();
        foreach (var certificate in options)
        {
            if (certificate is null || string.IsNullOrWhiteSpace(certificate.Path) || !string.IsNullOrEmpty(certificate.PasswordEnvironmentVariable))
                throw new InvalidDataException("Legacy certificate import requires an approved PFX placement and an explicit protected password.");
            var expected = Path.GetFullPath(Path.IsPathRooted(certificate.Path) ? certificate.Path : Path.Combine(request.Application.StateDirectory, certificate.Path));
            var placement = request.Placements.FirstOrDefault(item => string.Equals(Path.GetFullPath(Path.Combine(request.DestinationDirectory, item.RelativePath)), expected, StringComparison.Ordinal));
            if (placement is null || !request.AllowCredentialEscrow || !request.ApprovedFiles.Any(file => file.Credential && string.Equals(file.Name, placement.Name, StringComparison.Ordinal)))
                throw new InvalidDataException("Legacy certificate material is outside the explicitly approved credential inventory.");
            var bytes = PrivateOwnerFile.Read(Path.Combine(workspace.StagingDirectory, placement.RelativePath), 1, 1024 * 1024);
            try { certificates.Add(new ProxyManualCertificateInput(certificate.Id, bytes)); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        return certificates;
    }

    private static string ReadApprovedManualText(string path)
    {
        var bytes = PrivateOwnerFile.Read(path, 1, 4 * 1024 * 1024);
        try { return StrictManualUtf8.GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Approved manual text requires valid UTF8.", exception); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static ManualConfigurationReceipt DescribeManualImport(PortableManualConfigurationDocument document)
    {
        var bytes = StrictManualUtf8.GetBytes(document.Reveal());
        try { return new ManualConfigurationReceipt(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
