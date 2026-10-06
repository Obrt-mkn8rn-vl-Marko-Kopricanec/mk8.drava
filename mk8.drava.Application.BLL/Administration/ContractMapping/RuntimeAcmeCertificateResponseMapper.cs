using BusinessRuntimeAcmeCertificateProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeAcmeCertificateProjection;
using BusinessRuntimeAcmeProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeAcmeProjection;
using BusinessRuntimeCertificateProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeCertificateProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeAcmeCertificateResponseMapper
{
    public static RuntimeAcmeCertificateResponse FromProjection(BusinessRuntimeAcmeCertificateProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeAcmeCertificateResponse(id: projection.Id, enabled: projection.Enabled, domains: projection.Domains, renewBeforeDays: projection.RenewBeforeDays);
    }
}
