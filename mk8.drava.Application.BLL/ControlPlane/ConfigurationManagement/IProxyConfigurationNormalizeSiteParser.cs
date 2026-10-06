using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationNormalizeSiteParser
{
    ProxyConfigurationNormalizeSiteParseResult Parse(string text, ProxyConfigurationNormalizeFormat format);
}
