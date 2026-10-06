using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record ProxyAcmeStatusConfigurationSourceSnapshot
{
    public ProxyAcmeStatusConfigurationSourceSnapshot(bool Enabled, string DirectoryUrl, bool UseStaging, IEnumerable<ProxyAcmeConfiguredCertificateStatus> Certificates, IEnumerable<ProxyAcmeRuntimeCertificateSource> RuntimeCertificates)
    {
        ArgumentNullException.ThrowIfNull(Certificates);
        ArgumentNullException.ThrowIfNull(RuntimeCertificates);
        this.Enabled = Enabled;
        this.DirectoryUrl = DirectoryUrl;
        this.UseStaging = UseStaging;
        this.Certificates = AcmeList.Copy(Certificates.Select(RequireConfiguredCertificate));
        this.RuntimeCertificates = AcmeList.Copy(RuntimeCertificates.Select(RequireRuntimeCertificateSource));
    }

    public bool Enabled { get; }
    public string DirectoryUrl { get; }
    public bool UseStaging { get; }
    public IReadOnlyList<ProxyAcmeConfiguredCertificateStatus> Certificates { get; }
    public IReadOnlyList<ProxyAcmeRuntimeCertificateSource> RuntimeCertificates { get; }

    private static ProxyAcmeConfiguredCertificateStatus RequireConfiguredCertificate(ProxyAcmeConfiguredCertificateStatus certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate;
    }

    private static ProxyAcmeRuntimeCertificateSource RequireRuntimeCertificateSource(ProxyAcmeRuntimeCertificateSource certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate;
    }
}
