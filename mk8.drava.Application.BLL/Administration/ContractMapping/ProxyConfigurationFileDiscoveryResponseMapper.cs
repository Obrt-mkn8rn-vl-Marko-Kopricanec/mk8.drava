using BusinessProxyConfigurationDiscovery = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationDiscovery;
using BusinessProxyConfigurationFileDiscovery = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationFileDiscovery;
using BusinessProxyConfigurationFileError = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationFileError;
using BusinessProxyFilesystemLayout = Mk8.Drava.Application.BLL.Configuration.ProxyFilesystemLayout;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyConfigurationFileDiscoveryResponseMapper
{
    public static IReadOnlyList<ProxyConfigurationFileDiscoveryResponse> FromFiles(IReadOnlyList<BusinessProxyConfigurationFileDiscovery> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return ApiResponseList.Copy(files.Select(FromFile));
    }

    private static ProxyConfigurationFileDiscoveryResponse FromFile(BusinessProxyConfigurationFileDiscovery file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new ProxyConfigurationFileDiscoveryResponse(file.Path, file.Format, file.Status, file.Reason);
    }
}
