namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeAcmeResponse
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "Versioned administration wire contract preserves raw URI text; Application validates syntax and ownership before use.")]
    public RuntimeAcmeResponse(bool enabled, bool useStaging, string directoryUrl, IReadOnlyList<string> contactEmails, bool termsAccepted, string storagePath, int renewBeforeDays, int checkIntervalMinutes, int retryAfterMinutes, IReadOnlyList<RuntimeAcmeCertificateResponse> certificates)
    {
        Enabled = enabled;
        UseStaging = useStaging;
        DirectoryUrl = directoryUrl;
        ContactEmails = ApiResponseList.Copy(contactEmails);
        TermsAccepted = termsAccepted;
        StoragePath = storagePath;
        RenewBeforeDays = renewBeforeDays;
        CheckIntervalMinutes = checkIntervalMinutes;
        RetryAfterMinutes = retryAfterMinutes;
        Certificates = ApiResponseList.Copy(certificates);
    }

    public bool Enabled { get; }
    public bool UseStaging { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "Versioned wire URI text; not used as a destination without Application validation.")]
    public string DirectoryUrl { get; }
    public IReadOnlyList<string> ContactEmails { get; }
    public bool TermsAccepted { get; }
    public string StoragePath { get; }
    public int RenewBeforeDays { get; }
    public int CheckIntervalMinutes { get; }
    public int RetryAfterMinutes { get; }
    public IReadOnlyList<RuntimeAcmeCertificateResponse> Certificates { get; }
}
