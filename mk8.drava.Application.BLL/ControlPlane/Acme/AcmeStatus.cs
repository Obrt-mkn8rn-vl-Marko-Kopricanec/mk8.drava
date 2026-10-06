namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeStatus
{
    public AcmeStatus(bool Enabled, string DirectoryUrl, bool UseStaging, IEnumerable<AcmeCertificateLifecycleStatus> Certificates)
    {
        ArgumentNullException.ThrowIfNull(Certificates);
        this.Enabled = Enabled;
        this.DirectoryUrl = DirectoryUrl;
        this.UseStaging = UseStaging;
        this.Certificates = AcmeList.Copy(Certificates);
    }

    public bool Enabled { get; }
    public string DirectoryUrl { get; }
    public bool UseStaging { get; }
    public IReadOnlyList<AcmeCertificateLifecycleStatus> Certificates { get; }

    public static AcmeStatus FromSources(bool enabled, string directoryUrl, bool useStaging, IEnumerable<AcmeCertificateLifecycleStatus> certificates)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        return new AcmeStatus(enabled, directoryUrl, useStaging, certificates);
    }
}
