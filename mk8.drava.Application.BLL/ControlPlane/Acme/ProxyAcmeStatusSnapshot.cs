namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record ProxyAcmeStatusSnapshot
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "ACME runtime/status snapshots preserve configured directory text and empty disabled-state values; URI parsing and admission remain in operational validation and transport policy.")]
    public ProxyAcmeStatusSnapshot(bool Enabled, string DirectoryUrl, bool UseStaging, IEnumerable<ProxyAcmeConfiguredCertificateStatus> Certificates, IReadOnlyDictionary<string, ProxyAcmeRuntimeCertificateStatus> RuntimeCertificates)
    {
        ArgumentNullException.ThrowIfNull(Certificates);
        ArgumentNullException.ThrowIfNull(RuntimeCertificates);
        this.Enabled = Enabled;
        this.DirectoryUrl = DirectoryUrl;
        this.UseStaging = UseStaging;
        this.Certificates = AcmeList.Copy(Certificates);
        this.RuntimeCertificates = new Dictionary<string, ProxyAcmeRuntimeCertificateStatus>(RuntimeCertificates, StringComparer.OrdinalIgnoreCase);
    }

    public bool Enabled { get; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "ACME runtime/status snapshots preserve configured directory text and empty disabled-state values; URI parsing and admission remain in operational validation and transport policy.")]
    public string DirectoryUrl { get; }
    public bool UseStaging { get; }
    public IReadOnlyList<ProxyAcmeConfiguredCertificateStatus> Certificates { get; }
    public IReadOnlyDictionary<string, ProxyAcmeRuntimeCertificateStatus> RuntimeCertificates { get; }
}
