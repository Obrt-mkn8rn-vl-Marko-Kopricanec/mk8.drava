namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeStatus
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "ACME runtime/status snapshots preserve configured directory text and empty disabled-state values; URI parsing and admission remain in operational validation and transport policy.")]
    public AcmeStatus(bool Enabled, string DirectoryUrl, bool UseStaging, IEnumerable<AcmeCertificateLifecycleStatus> Certificates)
    {
        ArgumentNullException.ThrowIfNull(Certificates);
        this.Enabled = Enabled;
        this.DirectoryUrl = DirectoryUrl;
        this.UseStaging = UseStaging;
        this.Certificates = AcmeList.Copy(Certificates);
    }

    public bool Enabled { get; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "ACME runtime/status snapshots preserve configured directory text and empty disabled-state values; URI parsing and admission remain in operational validation and transport policy.")]
    public string DirectoryUrl { get; }
    public bool UseStaging { get; }
    public IReadOnlyList<AcmeCertificateLifecycleStatus> Certificates { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "ACME runtime/status snapshots preserve configured directory text and empty disabled-state values; URI parsing and admission remain in operational validation and transport policy.")]
    public static AcmeStatus FromSources(bool enabled, string directoryUrl, bool useStaging, IEnumerable<AcmeCertificateLifecycleStatus> certificates)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        return new AcmeStatus(enabled, directoryUrl, useStaging, certificates);
    }
}
