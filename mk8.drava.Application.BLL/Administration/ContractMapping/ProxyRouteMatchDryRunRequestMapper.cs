using System.Collections.ObjectModel;
using BusinessRouteMatchDryRunRequest = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunRequest;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyRouteMatchDryRunRequestMapper
{
    public static BusinessRouteMatchDryRunRequest ToRouteMatchDryRunRequest(this ProxyRouteMatchDryRunRequest submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return new BusinessRouteMatchDryRunRequest(submission.Scheme, submission.Host, submission.Port, submission.Method, submission.Path, submission.Query, submission.Headers ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), submission.ClientIp, submission.ListenerName, submission.Protocol);
    }
}
