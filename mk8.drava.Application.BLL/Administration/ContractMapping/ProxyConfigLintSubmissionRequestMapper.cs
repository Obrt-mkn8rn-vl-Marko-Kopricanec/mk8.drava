using BusinessConfigLintRequest = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ConfigLintRequest;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyConfigLintSubmissionRequestMapper
{
    public static BusinessConfigLintRequest ToConfigLintRequest(this ProxyConfigLintSubmissionRequest submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return new BusinessConfigLintRequest(submission.Format, submission.Text);
    }
}
