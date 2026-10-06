using BusinessRouteDiagnosticsStatus = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteDiagnosticsStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RouteDiagnosticsStatusResponseMapper
{
    public static RouteDiagnosticsStatusResponse FromStatus(BusinessRouteDiagnosticsStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new RouteDiagnosticsStatusResponse(status.Available);
    }
}
