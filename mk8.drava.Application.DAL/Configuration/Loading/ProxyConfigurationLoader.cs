using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.DAL.Acme;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;
public sealed partial class ProxyConfigurationLoader : IProxyConfigurationLoader, IProxyRestoreConfigurationValidator
{
    private readonly IMdravaDataDirectoryProvider _dataDirectoryProvider;
    private readonly ProxyDataDirectoryBootstrapper _bootstrapper;
    private readonly SiteConfigurationParser _siteParser;
    private readonly IProxyAdminUrlPolicy _adminUrlPolicy;
    private readonly IProxyEndpointAddressPolicy _endpointAddressPolicy;
    private readonly IProxyRelativeStoragePathPolicy _relativeStoragePathPolicy;
    private readonly IProxyUrlSyntaxPolicy _urlSyntaxPolicy;
    private readonly IProxyTrustedProxyPolicy _trustedProxyPolicy;
    private readonly ILogger<ProxyConfigurationLoader> _logger;
    private readonly TimeProvider _timeProvider;
    private int _nextVersion;
    public ProxyConfigurationLoader(IMdravaDataDirectoryProvider dataDirectoryProvider, ProxyDataDirectoryBootstrapper bootstrapper, SiteConfigurationParser siteParser, IProxyAdminUrlPolicy adminUrlPolicy, IProxyEndpointAddressPolicy endpointAddressPolicy, IProxyRelativeStoragePathPolicy relativeStoragePathPolicy, IProxyUrlSyntaxPolicy urlSyntaxPolicy, IProxyTrustedProxyPolicy trustedProxyPolicy, ILogger<ProxyConfigurationLoader> logger, TimeProvider timeProvider)
    {
        _dataDirectoryProvider = dataDirectoryProvider;
        _bootstrapper = bootstrapper;
        _siteParser = siteParser;
        _adminUrlPolicy = adminUrlPolicy;
        _endpointAddressPolicy = endpointAddressPolicy;
        _relativeStoragePathPolicy = relativeStoragePathPolicy;
        _urlSyntaxPolicy = urlSyntaxPolicy;
        _trustedProxyPolicy = trustedProxyPolicy;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ProxyConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        return await LoadCoreAsync(allocateVersion: true, ensureLayout: true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProxyConfigurationLoadResult> ValidateAsync(CancellationToken cancellationToken)
    {
        return await LoadCoreAsync(allocateVersion: false, ensureLayout: true, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ProxyConfigurationLoadResult> ValidateExistingLayoutAsync(CancellationToken cancellationToken)
    {
        return await LoadCoreAsync(allocateVersion: false, ensureLayout: false, cancellationToken).ConfigureAwait(false);
    }

    async ValueTask<ProxyRestoreConfigurationValidationResult> IProxyRestoreConfigurationValidator.ValidateExistingLayoutAsync(CancellationToken cancellationToken)
    {
        var loadResult = await ValidateExistingLayoutAsync(cancellationToken).ConfigureAwait(false);
        return ProxyRestoreConfigurationValidationResult.Completed(loadResult.Errors, loadResult.FileErrors, loadResult.WouldBeVersion);
    }

    private async ValueTask<ProxyConfigurationLoadResult> LoadCoreAsync(bool allocateVersion, bool ensureLayout, CancellationToken cancellationToken)
    {
        var sourceDirectory = _dataDirectoryProvider.GetSitesConfigDirectory();
        var operationalConfigPath = _dataDirectoryProvider.GetProxyOperationalConfigPath();
        var attemptedAtUtc = _timeProvider.GetUtcNow();
        var wouldBeVersion = Volatile.Read(ref _nextVersion) + 1;
        var bootstrapDiscovery = ensureLayout ? _bootstrapper.EnsureLayout() : _bootstrapper.InspectLayout();
        List<ProxyConfigurationFileDiscovery> discoveredFiles = [..bootstrapDiscovery.Files];
        var discoveredSiteFiles = SiteConfigurationFileDiscovery.DiscoverLoadableSiteFiles(sourceDirectory, discoveredFiles);
        var siteFiles = discoveredSiteFiles.Select(static file => file.Path).ToArray();
        List<SiteConfigurationSource> sites = [];
        List<ProxyConfigurationFileError> errors = [];
        foreach (var(siteFile, format)in discoveredSiteFiles)
        {
            var site = await ReadSiteAsync(siteFile, format, discoveredFiles, errors, cancellationToken).ConfigureAwait(false);
            if (site is not null)
            {
                sites.Add(SiteConfigurationSource.FromFile(siteFile, site));
            }
        }

        ProxyConfigurationDiscovery BuildDiscovery()
        {
            return bootstrapDiscovery.WithFiles(discoveredFiles);
        }

        if (errors.Count > 0)
        {
            return ProxyConfigurationLoadResult.Failed(sourceDirectory, attemptedAtUtc, siteFiles, BuildDiscovery(), errors, wouldBeVersion);
        }

        var listenerMergeFailures = ProxyConfigurationValidationRules.ValidateListenerMergeCompatibility(sites);
        if (listenerMergeFailures.Count > 0)
        {
            return ProxyConfigurationLoadResult.Failed(sourceDirectory, attemptedAtUtc, siteFiles, BuildDiscovery(), listenerMergeFailures.Select(static failure => ProxyConfigurationFileError.Global(failure)).ToArray(), wouldBeVersion);
        }

        var operationalOptions = await ReadOperationalOptionsAsync(operationalConfigPath, discoveredFiles, errors, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return ProxyConfigurationLoadResult.Failed(sourceDirectory, attemptedAtUtc, siteFiles, BuildDiscovery(), errors, wouldBeVersion);
        }

        var operationalFailures = ProxyOperationalOptionsValidationRules.Validate(operationalOptions, Environment.GetEnvironmentVariable, _adminUrlPolicy, _relativeStoragePathPolicy, _urlSyntaxPolicy, _trustedProxyPolicy);
        if (operationalFailures.Count > 0)
        {
            return ProxyConfigurationLoadResult.Failed(sourceDirectory, attemptedAtUtc, siteFiles, BuildDiscovery(), operationalFailures.Select(failure => ProxyConfigurationFileError.ForPath(operationalConfigPath, failure)).ToArray(), wouldBeVersion);
        }

        var options = SiteOptionsAggregator.ToProxyOptions(sites);
        var certificateValidationFailures = ProxyConfigurationValidationRules.ValidateTlsReferences(options, operationalOptions);
        if (certificateValidationFailures.Count > 0)
        {
            return ProxyConfigurationLoadResult.Failed(sourceDirectory, attemptedAtUtc, siteFiles, BuildDiscovery(), certificateValidationFailures.Select(static failure => ProxyConfigurationFileError.Global(failure)).ToArray(), wouldBeVersion);
        }

        if (siteFiles.Length > 0)
        {
            var validationFailures = ProxyOptionsValidationRules.Validate(options, _endpointAddressPolicy, _urlSyntaxPolicy);
            if (validationFailures.Count > 0)
            {
                return ProxyConfigurationLoadResult.Failed(sourceDirectory, attemptedAtUtc, siteFiles, BuildDiscovery(), validationFailures.Select(static failure => ProxyConfigurationFileError.Global(failure)).ToArray(), wouldBeVersion);
            }
        }
        else
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogNoProxySiteConfigurationFiles10000(_logger, sourceDirectory, null);
            }
        }

        var certificates = LoadCertificates(operationalOptions, _dataDirectoryProvider.GetDataDirectory(), errors);
        if (errors.Count > 0)
        {
            DisposeCertificates(certificates);
            return ProxyConfigurationLoadResult.Failed(sourceDirectory, attemptedAtUtc, siteFiles, BuildDiscovery(), errors, wouldBeVersion);
        }

        if (!allocateVersion)
        {
            DisposeCertificates(certificates);
            return ProxyConfigurationLoadResult.Validated(sourceDirectory, attemptedAtUtc, siteFiles, BuildDiscovery(), wouldBeVersion);
        }

        var version = Interlocked.Increment(ref _nextVersion);
        var snapshot = ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(options, operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, Environment.GetEnvironmentVariable), certificates, version, _timeProvider.GetUtcNow(), sourceDirectory, siteFiles, BuildDiscovery());
        return ProxyConfigurationLoadResult.Loaded(sourceDirectory, snapshot, BuildDiscovery());
    }

    private static Dictionary<string, RuntimeCertificate> LoadCertificates(ProxyOperationalOptions operationalOptions, string dataDirectory, List<ProxyConfigurationFileError> errors)
    {
        Dictionary<string, RuntimeCertificate> certificates = new(StringComparer.OrdinalIgnoreCase);
        foreach (var certificateOptions in operationalOptions.Certificates)
        {
            if (string.IsNullOrWhiteSpace(certificateOptions.Id) || !string.Equals(certificateOptions.Format, "pfx", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(certificateOptions.Path))
            {
                continue;
            }

            var certificatePath = Path.IsPathRooted(certificateOptions.Path) ? certificateOptions.Path : Path.Combine(dataDirectory, certificateOptions.Path);
            if (!File.Exists(certificatePath))
            {
                errors.Add(ProxyConfigurationFileError.Global($"Certificate '{certificateOptions.Id}' file does not exist: {certificatePath}"));
                continue;
            }

            var password = ResolveCertificatePassword(certificateOptions);
            var hasConfiguredPassword = !string.IsNullOrEmpty(certificateOptions.Password) || !string.IsNullOrWhiteSpace(certificateOptions.PasswordEnvironmentVariable);
            try
            {
                var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password, X509KeyStorageFlags.UserKeySet);
                if (!certificate.HasPrivateKey)
                {
                    certificate.Dispose();
                    errors.Add(ProxyConfigurationFileError.Global($"Certificate '{certificateOptions.Id}' must contain a private key."));
                    continue;
                }

                certificates.Add(certificateOptions.Id, RuntimeCertificateFactory.ManualPfx(certificateOptions.Id, certificatePath, hasConfiguredPassword, certificate));
            }
            catch (CryptographicException exception)
            {
                errors.Add(ProxyConfigurationFileError.Global($"Certificate '{certificateOptions.Id}' could not be loaded from '{certificatePath}': {exception.Message}"));
            }
        }

        foreach (var acmeCertificate in AcmeCertificateMaterialStore.LoadRuntimeCertificates(operationalOptions.Acme, dataDirectory, errors))
        {
            certificates[acmeCertificate.Key] = acmeCertificate.Value;
        }

        return certificates;
    }

    private static void DisposeCertificates(IReadOnlyDictionary<string, RuntimeCertificate> certificates)
    {
        foreach (var certificate in certificates.Values)
        {
            certificate.Certificate.Dispose();
        }
    }

    private static ReadOnlySpan<char> ResolveCertificatePassword(CertificateOptions certificateOptions)
    {
        if (!string.IsNullOrWhiteSpace(certificateOptions.PasswordEnvironmentVariable))
        {
            return Environment.GetEnvironmentVariable(certificateOptions.PasswordEnvironmentVariable).AsSpan();
        }

        return certificateOptions.Password.AsSpan();
    }

    private async ValueTask<ProxyOperationalOptions> ReadOperationalOptionsAsync(string operationalConfigPath, List<ProxyConfigurationFileDiscovery> discoveries, List<ProxyConfigurationFileError> errors, CancellationToken cancellationToken)
    {
        if (!File.Exists(operationalConfigPath))
        {
            discoveries.Add(new ProxyConfigurationFileDiscovery(operationalConfigPath, "json", "skipped", "Proxy operational configuration file does not exist; defaults are used."));
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Information))
            {
                LogProxyOperationalConfigurationFileWas10001(_logger, operationalConfigPath, null);
            }
            return new ProxyOperationalOptions();
        }

        try
        {
            var stream = File.OpenRead(operationalConfigPath);
            await using var streamDisposal = stream.ConfigureAwait(false);
            var options = await JsonSerializer.DeserializeAsync<ProxyOperationalOptions>(stream, SiteConfigurationParser.ReadJsonOptions, cancellationToken).ConfigureAwait(false);
            if (options is null)
            {
                errors.Add(ProxyConfigurationFileError.ForPath(operationalConfigPath, "Operational configuration did not contain a JSON object."));
                discoveries.Add(new ProxyConfigurationFileDiscovery(operationalConfigPath, "json", "failed", "Operational configuration did not contain a JSON object."));
                return new ProxyOperationalOptions();
            }

            discoveries.Add(new ProxyConfigurationFileDiscovery(operationalConfigPath, "json", "loaded", "Proxy operational configuration."));
            return options;
        }
        catch (JsonException exception)
        {
            errors.Add(ProxyConfigurationFileError.ForPath(operationalConfigPath, $"JSON is invalid: {exception.Message}"));
            discoveries.Add(new ProxyConfigurationFileDiscovery(operationalConfigPath, "json", "failed", $"JSON is invalid: {exception.Message}"));
            return new ProxyOperationalOptions();
        }
        catch (IOException exception)
        {
            errors.Add(ProxyConfigurationFileError.ForPath(operationalConfigPath, $"File could not be read: {exception.Message}"));
            discoveries.Add(new ProxyConfigurationFileDiscovery(operationalConfigPath, "json", "failed", $"File could not be read: {exception.Message}"));
            return new ProxyOperationalOptions();
        }
        catch (UnauthorizedAccessException exception)
        {
            errors.Add(ProxyConfigurationFileError.ForPath(operationalConfigPath, $"File could not be accessed: {exception.Message}"));
            discoveries.Add(new ProxyConfigurationFileDiscovery(operationalConfigPath, "json", "failed", $"File could not be accessed: {exception.Message}"));
            return new ProxyOperationalOptions();
        }
    }

    private async ValueTask<SiteOptions?> ReadSiteAsync(string siteFile, SiteConfigurationFormat format, List<ProxyConfigurationFileDiscovery> discoveries, List<ProxyConfigurationFileError> errors, CancellationToken cancellationToken)
    {
        try
        {
            var site = await _siteParser.ReadSiteFileAsync(siteFile, format, cancellationToken).ConfigureAwait(false);
            if (site is null)
            {
                errors.Add(ProxyConfigurationFileError.ForPath(siteFile, "Site configuration did not contain a JSON object."));
                discoveries.Add(new ProxyConfigurationFileDiscovery(siteFile, SiteConfigurationFileDiscovery.FormatName(format), "failed", "Site configuration did not contain a JSON object."));
                return null;
            }

            discoveries.Add(new ProxyConfigurationFileDiscovery(siteFile, SiteConfigurationFileDiscovery.FormatName(format), "loaded", "Site configuration."));
            return site;
        }
        catch (JsonException exception)
        {
            errors.Add(ProxyConfigurationFileError.ForPath(siteFile, $"JSON is invalid: {exception.Message}"));
            discoveries.Add(new ProxyConfigurationFileDiscovery(siteFile, SiteConfigurationFileDiscovery.FormatName(format), "failed", $"JSON is invalid: {exception.Message}"));
            return null;
        }
        catch (YamlException exception)
        {
            errors.Add(ProxyConfigurationFileError.ForPath(siteFile, $"YAML is invalid: {exception.Message}"));
            discoveries.Add(new ProxyConfigurationFileDiscovery(siteFile, SiteConfigurationFileDiscovery.FormatName(format), "failed", $"YAML is invalid: {exception.Message}"));
            return null;
        }
        catch (IOException exception)
        {
            errors.Add(ProxyConfigurationFileError.ForPath(siteFile, $"File could not be read: {exception.Message}"));
            discoveries.Add(new ProxyConfigurationFileDiscovery(siteFile, SiteConfigurationFileDiscovery.FormatName(format), "failed", $"File could not be read: {exception.Message}"));
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            errors.Add(ProxyConfigurationFileError.ForPath(siteFile, $"File could not be accessed: {exception.Message}"));
            discoveries.Add(new ProxyConfigurationFileDiscovery(siteFile, SiteConfigurationFileDiscovery.FormatName(format), "failed", $"File could not be accessed: {exception.Message}"));
            return null;
        }
    }
}
