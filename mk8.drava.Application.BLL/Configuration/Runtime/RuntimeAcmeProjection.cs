namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeAcmeProjection
{
    public RuntimeAcmeProjection(bool Enabled, bool UseStaging, string DirectoryUrl, IReadOnlyList<string> ContactEmails, bool TermsAccepted, string StoragePath, int RenewBeforeDays, int CheckIntervalMinutes, int RetryAfterMinutes, IReadOnlyList<RuntimeAcmeCertificateProjection> Certificates)
    {
        ArgumentNullException.ThrowIfNull(DirectoryUrl);
        ArgumentNullException.ThrowIfNull(StoragePath);
        this.Enabled = Enabled;
        this.UseStaging = UseStaging;
        this.DirectoryUrl = DirectoryUrl;
        this.ContactEmails = RuntimeList.Copy(ContactEmails);
        this.TermsAccepted = TermsAccepted;
        this.StoragePath = StoragePath;
        this.RenewBeforeDays = RenewBeforeDays;
        this.CheckIntervalMinutes = CheckIntervalMinutes;
        this.RetryAfterMinutes = RetryAfterMinutes;
        this.Certificates = RuntimeList.Copy(Certificates);
    }

    public bool Enabled { get; }
    public bool UseStaging { get; }
    public string DirectoryUrl { get; }
    public IReadOnlyList<string> ContactEmails { get; }
    public bool TermsAccepted { get; }
    public string StoragePath { get; }
    public int RenewBeforeDays { get; }
    public int CheckIntervalMinutes { get; }
    public int RetryAfterMinutes { get; }
    public IReadOnlyList<RuntimeAcmeCertificateProjection> Certificates { get; }
}
