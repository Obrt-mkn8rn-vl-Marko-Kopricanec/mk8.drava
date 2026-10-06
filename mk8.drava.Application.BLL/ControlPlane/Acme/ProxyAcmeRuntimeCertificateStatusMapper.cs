namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public static class ProxyAcmeRuntimeCertificateStatusMapper
{
    public static IReadOnlyDictionary<string, ProxyAcmeRuntimeCertificateStatus> FromSources(IEnumerable<ProxyAcmeRuntimeCertificateSource> runtimeCertificates)
    {
        ArgumentNullException.ThrowIfNull(runtimeCertificates);
        return runtimeCertificates.Select(RequireRuntimeCertificateSource).ToDictionary(static certificate => certificate.Key, static certificate => new ProxyAcmeRuntimeCertificateStatus(certificate.Id, certificate.Source, certificate.NotBeforeUtc, certificate.NotAfterUtc), StringComparer.OrdinalIgnoreCase);
    }

    private static ProxyAcmeRuntimeCertificateSource RequireRuntimeCertificateSource(ProxyAcmeRuntimeCertificateSource certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate;
    }
}
