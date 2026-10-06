using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public static class AcmeRenewalConfigurationInputMapper
{
    public static AcmeRenewalConfigurationInput FromSources(AcmeRenewalConfigurationSourceSet source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AcmeRenewalConfigurationInput(source.Enabled, source.StoragePath, source.DirectoryUrl, source.ContactEmails, source.TermsAccepted, source.RetryAfterMinutes, source.Certificates.Select(ToCertificateInput));
    }

    private static AcmeRenewalCertificateInput ToCertificateInput(AcmeRenewalCertificateSource certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return new AcmeRenewalCertificateInput(certificate.Id, certificate.Enabled, certificate.Domains, certificate.RenewBeforeDays, certificate.ActiveCertificate);
    }
}
