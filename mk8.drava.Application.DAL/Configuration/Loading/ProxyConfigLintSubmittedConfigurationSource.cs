using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using YamlDotNet.Core;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;
public sealed class ProxyConfigLintSubmittedConfigurationSource : IProxyConfigLintSubmittedConfigurationSource
{
    private readonly SiteConfigurationParser _siteParser;
    private readonly IRuntimeHttp3PlatformSupportSource _http3PlatformSupportSource;
    private readonly IProxyEndpointAddressPolicy _endpointAddressPolicy;
    private readonly IProxyUrlSyntaxPolicy _urlSyntaxPolicy;
    public ProxyConfigLintSubmittedConfigurationSource(SiteConfigurationParser siteParser, IRuntimeHttp3PlatformSupportSource http3PlatformSupportSource, IProxyEndpointAddressPolicy endpointAddressPolicy, IProxyUrlSyntaxPolicy urlSyntaxPolicy)
    {
        _siteParser = siteParser;
        _http3PlatformSupportSource = http3PlatformSupportSource;
        _endpointAddressPolicy = endpointAddressPolicy;
        _urlSyntaxPolicy = urlSyntaxPolicy;
    }

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
        var validationErrors = ProxyOptionsValidationRules.Validate(options, _endpointAddressPolicy, _urlSyntaxPolicy).Select(failure => ProxyConfigurationFileError.ForPath(source.Path, failure)).ToArray();
        var operationalOptions = new ProxyOperationalOptions();
        var adminSecurity = ProxyConfigurationRuntimeMapper.ToRuntimeAdminSecurityOptions(operationalOptions.Admin, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null));
        var metrics = ProxyConfigurationRuntimeMapper.ToRuntimeMetricsOptions(operationalOptions.Metrics);
        var listeners = ProxyConfigurationRuntimeMapper.ToRuntimeListeners(options.Listeners);
        var routes = ProxyConfigurationRuntimeMapper.ToRuntimeRoutes(options.Routes, operationalOptions);
        return ProxyConfigLintSubmittedConfigurationResult.Loaded(ProxyConfigLintConfigurationSnapshotMapper.ToLintSnapshot(ProxyConfigLintRuntimeConfigurationSourceMapper.FromSources([source.Path], adminSecurity.Urls, adminSecurity.RequireAuthentication, metrics.PublicMetricsEnabled, listeners, routes), _http3PlatformSupportSource.Read()), validationErrors);
    }
}
