namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record AcmeStatusResponse
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "Versioned administration wire contract preserves raw URI text; Application validates syntax and ownership before use.")]
    public AcmeStatusResponse(bool enabled, string directoryUrl, bool useStaging, IReadOnlyList<AcmeCertificateLifecycleStatusResponse> certificates)
    {
        Enabled = enabled;
        DirectoryUrl = directoryUrl;
        UseStaging = useStaging;
        Certificates = ApiResponseList.Copy(certificates);
    }

    public bool Enabled { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "Versioned wire URI text; not used as a destination without Application validation.")]
    public string DirectoryUrl { get; }
    public bool UseStaging { get; }
    public IReadOnlyList<AcmeCertificateLifecycleStatusResponse> Certificates { get; }
}
