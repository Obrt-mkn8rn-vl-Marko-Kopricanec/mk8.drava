using BusinessProxyConfigurationNormalizeRequest = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationNormalizeRequest;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyConfigurationNormalizeSubmissionRequestMapper
{
    public static BusinessProxyConfigurationNormalizeRequest ToNormalizeRequest(this ProxyConfigurationNormalizeSubmissionRequest submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return new BusinessProxyConfigurationNormalizeRequest(submission.Format, submission.Text);
    }
}
