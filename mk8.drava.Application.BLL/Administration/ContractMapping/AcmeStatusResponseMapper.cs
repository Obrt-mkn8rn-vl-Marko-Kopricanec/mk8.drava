using BusinessAcmeCertificateLifecycleStatus = Mk8.Drava.Application.BLL.ControlPlane.Acme.AcmeCertificateLifecycleStatus;
using BusinessAcmeStatus = Mk8.Drava.Application.BLL.ControlPlane.Acme.AcmeStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class AcmeStatusResponseMapper
{
    public static AcmeStatusResponse FromStatus(BusinessAcmeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new AcmeStatusResponse(enabled: status.Enabled, directoryUrl: status.DirectoryUrl, useStaging: status.UseStaging, certificates: AcmeCertificateLifecycleStatusResponseMapper.FromStatuses(status.Certificates));
    }
}
