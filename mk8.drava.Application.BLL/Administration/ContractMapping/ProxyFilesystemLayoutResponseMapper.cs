using BusinessProxyConfigurationDiscovery = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationDiscovery;
using BusinessProxyConfigurationFileDiscovery = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationFileDiscovery;
using BusinessProxyConfigurationFileError = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationFileError;
using BusinessProxyFilesystemLayout = Mk8.Drava.Application.BLL.Configuration.ProxyFilesystemLayout;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyFilesystemLayoutResponseMapper
{
    public static ProxyFilesystemLayoutResponse FromLayout(BusinessProxyFilesystemLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new ProxyFilesystemLayoutResponse(layout.DataDirectory, layout.ConfigDirectory, layout.SitesDirectory, layout.LogsDirectory, layout.CertificatesDirectory, layout.StateDirectory, layout.ProxyConfigPath);
    }
}
