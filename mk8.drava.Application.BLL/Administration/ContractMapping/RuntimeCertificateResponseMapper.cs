using BusinessRuntimeAcmeCertificateProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeAcmeCertificateProjection;
using BusinessRuntimeAcmeProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeAcmeProjection;
using BusinessRuntimeCertificateProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeCertificateProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeCertificateResponseMapper
{
    public static IReadOnlyList<RuntimeCertificateResponse> FromCertificates(IReadOnlyList<BusinessRuntimeCertificateProjection> certificates)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        return ApiResponseList.Copy(certificates.Select(FromCertificate));
    }

    private static RuntimeCertificateResponse FromCertificate(BusinessRuntimeCertificateProjection certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return new RuntimeCertificateResponse(id: certificate.Id, path: certificate.Path, format: certificate.Format, source: certificate.Source, domains: certificate.Domains, hasConfiguredPassword: certificate.HasConfiguredPassword, subject: certificate.Subject, thumbprint: certificate.Thumbprint, notBefore: certificate.NotBefore, notAfter: certificate.NotAfter);
    }
}
