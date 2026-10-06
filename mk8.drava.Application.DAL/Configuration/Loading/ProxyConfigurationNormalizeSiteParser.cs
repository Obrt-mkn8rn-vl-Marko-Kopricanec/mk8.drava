using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using System.Text.Json;
using YamlDotNet.Core;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;
public sealed class ProxyConfigurationNormalizeSiteParser : IProxyConfigurationNormalizeSiteParser
{
    private readonly SiteConfigurationParser _siteParser;
    public ProxyConfigurationNormalizeSiteParser(SiteConfigurationParser siteParser)
    {
        _siteParser = siteParser;
    }

    public ProxyConfigurationNormalizeSiteParseResult Parse(string text, ProxyConfigurationNormalizeFormat format)
    {
        var siteFormat = format == ProxyConfigurationNormalizeFormat.Yaml ? SiteConfigurationFormat.Yaml : SiteConfigurationFormat.Json;
        try
        {
            var site = _siteParser.ReadSiteText(text, siteFormat);
            if (site is null)
            {
                return ProxyConfigurationNormalizeSiteParseResult.Failed("Site configuration did not contain an object.");
            }

            var canonicalJson = JsonSerializer.Serialize(site, SiteConfigurationParser.WriteJsonOptions);
            return ProxyConfigurationNormalizeSiteParseResult.Parsed(site, canonicalJson);
        }
        catch (JsonException exception)
        {
            return ProxyConfigurationNormalizeSiteParseResult.Failed($"JSON is invalid: {exception.Message}");
        }
        catch (YamlException exception)
        {
            return ProxyConfigurationNormalizeSiteParseResult.Failed($"YAML is invalid: {exception.Message}");
        }
    }
}
