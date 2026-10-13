using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using YamlDotNet.Core;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;
public sealed class ProxyConfigLintSubmittedConfigurationSource(SiteConfigurationParser siteParser, IRuntimeHttp3PlatformSupportSource http3PlatformSupportSource, IProxyEndpointAddressPolicy endpointAddressPolicy, IProxyUrlSyntaxPolicy urlSyntaxPolicy, IUpstreamTrustMaterialSource? upstreamTrust = null) : IProxyConfigLintSubmittedConfigurationSource
{
    private readonly SiteConfigurationParser _siteParser = siteParser;
    private readonly IRuntimeHttp3PlatformSupportSource _http3PlatformSupportSource = http3PlatformSupportSource;
    private readonly IProxyEndpointAddressPolicy _endpointAddressPolicy = endpointAddressPolicy;
    private readonly IProxyUrlSyntaxPolicy _urlSyntaxPolicy = urlSyntaxPolicy;
    private readonly IUpstreamTrustMaterialSource? _upstreamTrust = upstreamTrust;

    public ProxyConfigLintSubmittedConfigurationResult Read(string text, ProxyConfigurationNormalizeFormat format, DateTimeOffset loadedAtUtc)
    {
        var siteFormat = format == ProxyConfigurationNormalizeFormat.Yaml ? SiteConfigurationFormat.Yaml : SiteConfigurationFormat.Json;
        SiteOptions? site;
        try
        {
            site = _siteParser.ReadSiteText(text, siteFormat);
        }
        catch (JsonException exception)
        {
            return ProxyConfigLintSubmittedConfigurationResult.Failed(ProxyConfigLintSubmittedConfigurationFailureKind.JsonParseError, exception.Message);
        }
        catch (YamlException exception)
        {
            return ProxyConfigLintSubmittedConfigurationResult.Failed(ProxyConfigLintSubmittedConfigurationFailureKind.YamlParseError, exception.Message);
        }

        if (site is null)
        {
            return ProxyConfigLintSubmittedConfigurationResult.Failed(ProxyConfigLintSubmittedConfigurationFailureKind.EmptySite, "Submitted config did not contain a site object.");
        }

        var source = SiteConfigurationSource.FromLintInput(site);
        var options = SiteOptionsAggregator.ToProxyOptions([source]);
        var validationErrors = ProxyOptionsValidationRules.Validate(options, _endpointAddressPolicy, _urlSyntaxPolicy).Select(failure => ProxyConfigurationFileError.ForPath(source.Path, failure)).ToList();
        var operationalOptions = new ProxyOperationalOptions();
        var adminSecurity = ProxyConfigurationRuntimeMapper.ToRuntimeAdminSecurityOptions(operationalOptions.Admin, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null));
        var metrics = ProxyConfigurationRuntimeMapper.ToRuntimeMetricsOptions(operationalOptions.Metrics);
        var listeners = ProxyConfigurationRuntimeMapper.ToRuntimeListeners(options.Listeners);
        var routes = ResolveRoutes(options, operationalOptions, source.Path, validationErrors);
        return ProxyConfigLintSubmittedConfigurationResult.Loaded(ProxyConfigLintConfigurationSnapshotMapper.ToLintSnapshot(ProxyConfigLintRuntimeConfigurationSourceMapper.FromSources([source.Path], adminSecurity.Urls, adminSecurity.RequireAuthentication, metrics.PublicMetricsEnabled, listeners, routes), _http3PlatformSupportSource.Read()), validationErrors);
    }

    private IReadOnlyList<RuntimeRoute> ResolveRoutes(ProxyOptions options, ProxyOperationalOptions operationalOptions, string path, List<ProxyConfigurationFileError> errors)
    {
        try { return ProxyConfigurationRuntimeMapper.ToRuntimeRoutes(options.Routes, operationalOptions, _upstreamTrust); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            errors.Add(ProxyConfigurationFileError.ForPath(path, $"Runtime configuration could not be resolved: {exception.Message}"));
            return [];
        }
    }
}
