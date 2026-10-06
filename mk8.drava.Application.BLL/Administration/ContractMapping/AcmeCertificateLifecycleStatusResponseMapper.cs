using BusinessAcmeCertificateLifecycleStatus = Mk8.Drava.Application.BLL.ControlPlane.Acme.AcmeCertificateLifecycleStatus;
using BusinessAcmeStatus = Mk8.Drava.Application.BLL.ControlPlane.Acme.AcmeStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class AcmeCertificateLifecycleStatusResponseMapper
{
    public static IReadOnlyList<AcmeCertificateLifecycleStatusResponse> FromStatuses(IReadOnlyList<BusinessAcmeCertificateLifecycleStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return ApiResponseList.Copy(statuses.Select(FromStatus));
    }

    private static AcmeCertificateLifecycleStatusResponse FromStatus(BusinessAcmeCertificateLifecycleStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new AcmeCertificateLifecycleStatusResponse(certificateId: status.CertificateId, enabled: status.Enabled, domains: status.Domains, active: status.Active, source: status.Source, notBeforeUtc: status.NotBeforeUtc, notAfterUtc: status.NotAfterUtc, renewalDueAtUtc: status.RenewalDueAtUtc, lastAttemptAtUtc: status.LastAttemptAtUtc, lastSucceededAtUtc: status.LastSucceededAtUtc, lastFailedAtUtc: status.LastFailedAtUtc, nextAttemptNotBeforeUtc: status.NextAttemptNotBeforeUtc, lastResult: status.LastResult, errorSummary: status.ErrorSummary);
    }
}
