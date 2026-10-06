using BusinessProxyConfigurationDiscovery = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationDiscovery;
using BusinessProxyConfigurationFileDiscovery = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationFileDiscovery;
using BusinessProxyConfigurationFileError = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationFileError;
using BusinessProxyFilesystemLayout = Mk8.Drava.Application.BLL.Configuration.ProxyFilesystemLayout;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyConfigurationDiscoveryResponseMapper
{
    public static ProxyConfigurationDiscoveryResponse FromDiscovery(BusinessProxyConfigurationDiscovery discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        return new ProxyConfigurationDiscoveryResponse(layout: ProxyFilesystemLayoutResponseMapper.FromLayout(discovery.Layout), files: ProxyConfigurationFileDiscoveryResponseMapper.FromFiles(discovery.Files), createdPaths: discovery.CreatedPaths, existingPaths: discovery.ExistingPaths);
    }
}
