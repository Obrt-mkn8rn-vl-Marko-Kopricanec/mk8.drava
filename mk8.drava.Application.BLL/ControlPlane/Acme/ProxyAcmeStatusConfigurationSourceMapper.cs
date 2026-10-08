using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public static class ProxyAcmeStatusConfigurationSourceMapper
{
    public static ProxyAcmeStatusConfigurationSourceSnapshot FromSources(RuntimeAcmeOptions acme, IEnumerable<KeyValuePair<string, RuntimeCertificate>> runtimeCertificates)
    {
        ArgumentNullException.ThrowIfNull(acme);
        ArgumentNullException.ThrowIfNull(runtimeCertificates);
        return new ProxyAcmeStatusConfigurationSourceSnapshot(acme.Enabled, acme.DirectoryUrl, acme.UseStaging, acme.Certificates.Select(ToConfiguredCertificateStatus), runtimeCertificates.Select(ToRuntimeCertificateSource));
    }

    private static ProxyAcmeConfiguredCertificateStatus ToConfiguredCertificateStatus(RuntimeAcmeCertificateOptions certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return new ProxyAcmeConfiguredCertificateStatus(certificate.Id, certificate.Enabled, certificate.Domains, certificate.RenewBeforeDays);
    }

    private static ProxyAcmeRuntimeCertificateSource ToRuntimeCertificateSource(KeyValuePair<string, RuntimeCertificate> certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate.Value, nameof(certificate));
        return new ProxyAcmeRuntimeCertificateSource(certificate.Key, certificate.Value.Id, certificate.Value.Source, new DateTimeOffset(certificate.Value.Certificate.NotBefore.ToUniversalTime()), new DateTimeOffset(certificate.Value.Certificate.NotAfter.ToUniversalTime()));
    }
}
