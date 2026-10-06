using BusinessProxyConfigurationDiscovery = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationDiscovery;
using BusinessProxyConfigurationFileDiscovery = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationFileDiscovery;
using BusinessProxyConfigurationFileError = Mk8.Drava.Application.BLL.Configuration.ProxyConfigurationFileError;
using BusinessProxyFilesystemLayout = Mk8.Drava.Application.BLL.Configuration.ProxyFilesystemLayout;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyConfigurationFileErrorResponseMapper
{
    public static IReadOnlyList<ProxyConfigurationFileErrorResponse> FromErrors(IReadOnlyList<BusinessProxyConfigurationFileError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return ApiResponseList.Copy(errors.Select(FromError));
    }

    private static ProxyConfigurationFileErrorResponse FromError(BusinessProxyConfigurationFileError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ProxyConfigurationFileErrorResponse(error.Path, error.Message);
    }
}
