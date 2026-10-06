using BusinessRuntimeAcmeCertificateProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeAcmeCertificateProjection;
using BusinessRuntimeAcmeProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeAcmeProjection;
using BusinessRuntimeCertificateProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeCertificateProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeAcmeResponseMapper
{
    public static RuntimeAcmeResponse FromProjection(BusinessRuntimeAcmeProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeAcmeResponse(enabled: projection.Enabled, useStaging: projection.UseStaging, directoryUrl: projection.DirectoryUrl, contactEmails: projection.ContactEmails, termsAccepted: projection.TermsAccepted, storagePath: projection.StoragePath, renewBeforeDays: projection.RenewBeforeDays, checkIntervalMinutes: projection.CheckIntervalMinutes, retryAfterMinutes: projection.RetryAfterMinutes, certificates: ApiResponseList.Copy(projection.Certificates.Select(RuntimeAcmeCertificateResponseMapper.FromProjection)));
    }
}
