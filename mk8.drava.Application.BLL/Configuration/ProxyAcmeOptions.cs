namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyAcmeOptions
{
    public bool Enabled { get; init; }
    public bool UseStaging { get; init; } = true;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "Configuration retains optional raw directory text, including absent disabled-state input; operational validation owns explicit HTTPS syntax and rejects invalid supplied text before use.")]
    public string? DirectoryUrl { get; init; }
    public IList<string> ContactEmails { get; init; } = [];
    public bool TermsAccepted { get; init; }
    public string StoragePath { get; init; } = "acme";
    public int RenewBeforeDays { get; init; } = 30;
    public int CheckIntervalMinutes { get; init; } = 720;
    public int RetryAfterMinutes { get; init; } = 60;
    public IList<AcmeManagedCertificateOptions> Certificates { get; init; } = [];
}
