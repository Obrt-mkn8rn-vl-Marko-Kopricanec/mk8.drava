using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using YamlDotNet.Core;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;

public sealed partial class ProxyConfigurationLoader
{
    private const string ProtectedDirectory = "portable-collection:manual/sites";
    private const string ProtectedOperational = "portable-collection:manual/operational";

    private async ValueTask<ProxyConfigurationLoadResult> LoadProtectedAsync(bool allocateVersion, CancellationToken cancellationToken)
    {
        var attempted = _timeProvider.GetUtcNow();
        var version = Volatile.Read(ref _nextVersion) + 1;
        var layout = new ProxyFilesystemLayout(_dataDirectoryProvider.GetDataDirectory(), "portable-collection:manual", ProtectedDirectory,
            _dataDirectoryProvider.GetLogsDirectory(), "portable-collection:manual/certificates", _dataDirectoryProvider.GetStateDirectory(), ProtectedOperational);
        var discoveries = new List<ProxyConfigurationFileDiscovery>();
        var errors = new List<ProxyConfigurationFileError>();
        var discovery = new ProxyConfigurationDiscovery(layout, discoveries, [], []);
        ProxyManualConfigurationInput input;
        try { input = await (_manualSource ?? throw new InvalidOperationException("Protected manual source is absent.")).ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (InvalidDataException)
        {
            return ProxyConfigurationLoadResult.Failed(ProtectedDirectory, attempted, [], discovery,
                [ProxyConfigurationFileError.Global("Protected manual input is invalid.")], version);
        }
        var sites = ReadProtectedSites(input, discoveries, errors);
        var paths = sites.Select(static site => site.Path).ToArray();
        var operational = ReadProtectedOperational(input, discoveries, errors);
        if (errors.Count == 0) errors.AddRange(ProxyConfigurationValidationRules.ValidateListenerMergeCompatibility(sites).Select(ProxyConfigurationFileError.Global));
        if (errors.Count == 0)
        {
            errors.AddRange(ProxyOperationalOptionsValidationRules.Validate(operational, static _ => null,
            _adminUrlPolicy, _relativeStoragePathPolicy, _urlSyntaxPolicy, _trustedProxyPolicy).Select(ProxyConfigurationFileError.Global));
        }

        var options = SiteOptionsAggregator.ToProxyOptions(sites);
        if (errors.Count == 0) errors.AddRange(ValidateSiteOptions(options, operational, sites.Count, ProtectedDirectory).Select(ProxyConfigurationFileError.Global));
        discovery = discovery.WithFiles(discoveries);
        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count != 0) return ProxyConfigurationLoadResult.Failed(ProtectedDirectory, attempted, paths, discovery, errors, version);
        return BuildRuntimeResult(options, operational, allocateVersion, attempted, ProtectedDirectory, paths, discovery, version, input);
    }

    private static List<SiteConfigurationSource> ReadProtectedSites(ProxyManualConfigurationInput input,
        List<ProxyConfigurationFileDiscovery> discoveries, List<ProxyConfigurationFileError> errors)
    {
        var sites = new List<SiteConfigurationSource>(input.Sites.Count);
        foreach (var item in input.Sites)
        {
            var path = ProtectedDirectory + "/" + item.Name;
            try
            {
                var parsed = SiteConfigurationParser.ReadProtectedSiteText(item.Document, item.Format == ProxyManualSiteFormat.Json ? SiteConfigurationFormat.Json : SiteConfigurationFormat.Yaml) ?? throw new JsonException("Empty protected site.");
                sites.Add(SiteConfigurationSource.FromFile(path, parsed));
                discoveries.Add(new ProxyConfigurationFileDiscovery(path, item.Format == ProxyManualSiteFormat.Json ? "json" : "yaml", "loaded", "Protected site configuration."));
            }
            catch (Exception exception) when (exception is JsonException or YamlException)
            {
                errors.Add(ProxyConfigurationFileError.ForPath(path, "Protected site configuration is invalid."));
                discoveries.Add(new ProxyConfigurationFileDiscovery(path, "protected", "failed", "Protected site configuration is invalid."));
            }
        }
        return sites;
    }

    private static ProxyOperationalOptions ReadProtectedOperational(ProxyManualConfigurationInput input,
        List<ProxyConfigurationFileDiscovery> discoveries, List<ProxyConfigurationFileError> errors)
    {
        try
        {
            var value = JsonSerializer.Deserialize<ProxyOperationalOptions>(input.OperationalDocument, SiteConfigurationParser.ReadJsonOptions)
                ?? throw new JsonException("Empty protected operational input.");
            RequireProtectedOperational(value);
            discoveries.Add(new ProxyConfigurationFileDiscovery(ProtectedOperational, "json", "loaded", "Protected operational configuration."));
            return new ProxyOperationalOptions
            {
                Admin = new ProxyAdminOptions { Urls = value.Admin.Urls, RequireAuthentication = value.Admin.RequireAuthentication,
                    Token = input.Administrator.Token, TokenEnvironmentVariable = "DISABLED", RecentAuditCapacity = value.Admin.RecentAuditCapacity, },
                Acme = value.Acme, Metrics = value.Metrics, Timeouts = value.Timeouts, Connections = value.Connections,
                Observability = value.Observability, Limits = value.Limits, ForwardedHeaders = value.ForwardedHeaders,
                Certificates = [.. value.Certificates.Select(static certificate => new CertificateOptions
                { Id = certificate.Id, Format = certificate.Format, Path = "portable-collection:manual/" + certificate.Id, Password = certificate.Password })],
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            errors.Add(ProxyConfigurationFileError.ForPath(ProtectedOperational, "Protected operational configuration is invalid or contains retired credential locators."));
            return new ProxyOperationalOptions();
        }
    }

    private static void RequireProtectedOperational(ProxyOperationalOptions value)
    {
        if (value.Admin is null || value.Acme is null || value.Metrics is null || value.Timeouts is null || value.Connections is null
            || value.Observability is null || value.Limits is null || value.ForwardedHeaders is null || value.Certificates is null)
        {
            throw new InvalidDataException("Protected operational sections are absent.");
        }

        if (value.Admin.Urls is null || value.Acme.ContactEmails is null || value.Acme.Certificates is null
            || value.Observability.LogPersistence is null || value.ForwardedHeaders.TrustedProxies is null)
        {
            throw new InvalidDataException("Protected operational collections are absent.");
        }

        if (!string.IsNullOrEmpty(value.Admin.Token)
            || (!string.IsNullOrEmpty(value.Admin.TokenEnvironmentVariable)
                && !string.Equals(value.Admin.TokenEnvironmentVariable, "DISABLED", StringComparison.Ordinal)
                && !string.Equals(value.Admin.TokenEnvironmentVariable, ProxyAdminSecurityTokenPolicy.DefaultTokenEnvironmentVariable, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Administrator credentials belong to the immutable Application bootstrap.");
        }

        foreach (var certificate in value.Certificates)
        {
            if (certificate is null || !string.IsNullOrEmpty(certificate.Path) || !string.IsNullOrEmpty(certificate.PasswordEnvironmentVariable))
                throw new InvalidDataException("Protected certificates require collection material rather than external locators.");
        }
        foreach (var certificate in value.Acme.Certificates)
        {
            if (certificate is null || certificate.Domains is null) throw new InvalidDataException("Protected ACME certificate input is absent.");
        }
    }

    private static Dictionary<string, RuntimeCertificate> LoadProtectedCertificates(ProxyOperationalOptions options,
        ProxyManualConfigurationInput input, List<ProxyConfigurationFileError> errors)
    {
        var certificates = new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var configured in options.Certificates)
            {
                X509Certificate2? owned = null;
                try
                {
                    var material = input.ResolveCertificate(configured.Id);
                    owned = OpenProtectedCertificate(material, configured.Password);
                    certificates.Add(configured.Id, RuntimeCertificateFactory.ManualPfx(configured.Id,
                        "portable-collection:manual/" + configured.Id, !string.IsNullOrEmpty(configured.Password), owned));
                    owned = null;
                }
                catch (Exception exception) when (exception is CryptographicException or InvalidDataException)
                {
                    errors.Add(ProxyConfigurationFileError.Global("Protected manual certificate cannot be resolved or loaded."));
                }
                finally { owned?.Dispose(); }
            }
            if (!options.Acme.Enabled) return certificates;
            foreach (var configured in options.Acme.Certificates.Where(static certificate => certificate.Enabled))
            {
                var material = input.FindCertificate(configured.Id);
                if (material is null) continue;
                X509Certificate2? owned = null;
                try
                {
                    owned = OpenProtectedCertificate(material, password: null);
                    certificates.Add(configured.Id, RuntimeCertificateFactory.Acme(configured.Id, owned, [.. configured.Domains]));
                    owned = null;
                }
                catch (Exception exception) when (exception is CryptographicException or InvalidDataException)
                {
                    errors.Add(ProxyConfigurationFileError.Global("Protected ACME certificate cannot be loaded."));
                }
                finally { owned?.Dispose(); }
            }
            return certificates;
        }
        catch
        {
            DisposeCertificates(certificates);
            throw;
        }
    }

    private static X509Certificate2 OpenProtectedCertificate(ProxyManualCertificateInput input, string? password)
    {
        var bytes = input.CopyPfx();
        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12(bytes, password.AsSpan(), X509KeyStorageFlags.EphemeralKeySet);
            if (certificate.HasPrivateKey) return certificate;
            certificate.Dispose();
            throw new InvalidDataException("Protected certificate has no private key.");
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
