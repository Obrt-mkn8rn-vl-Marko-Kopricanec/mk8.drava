using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record ProxyAcmeStatusConfigurationSourceSnapshot
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "ACME runtime/status snapshots preserve configured directory text and empty disabled-state values; URI parsing and admission remain in operational validation and transport policy.")]
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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "ACME runtime/status snapshots preserve configured directory text and empty disabled-state values; URI parsing and admission remain in operational validation and transport policy.")]
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
