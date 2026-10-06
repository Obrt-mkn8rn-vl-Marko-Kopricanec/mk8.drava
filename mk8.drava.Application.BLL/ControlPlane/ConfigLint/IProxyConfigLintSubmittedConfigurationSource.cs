using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public interface IProxyConfigLintSubmittedConfigurationSource
{
    ProxyConfigLintSubmittedConfigurationResult Read(string text, ProxyConfigurationNormalizeFormat format, DateTimeOffset loadedAtUtc);
}
