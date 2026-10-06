namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record AcmeCertificateLifecycleStatusResponse
{
    public AcmeCertificateLifecycleStatusResponse(string certificateId, bool enabled, IReadOnlyList<string> domains, bool active, string source, DateTimeOffset? notBeforeUtc, DateTimeOffset? notAfterUtc, DateTimeOffset? renewalDueAtUtc, DateTimeOffset? lastAttemptAtUtc, DateTimeOffset? lastSucceededAtUtc, DateTimeOffset? lastFailedAtUtc, DateTimeOffset? nextAttemptNotBeforeUtc, string lastResult, string? errorSummary)
    {
        CertificateId = certificateId;
        Enabled = enabled;
        Domains = ApiResponseList.Copy(domains);
        Active = active;
        Source = source;
        NotBeforeUtc = notBeforeUtc;
        NotAfterUtc = notAfterUtc;
        RenewalDueAtUtc = renewalDueAtUtc;
        LastAttemptAtUtc = lastAttemptAtUtc;
        LastSucceededAtUtc = lastSucceededAtUtc;
        LastFailedAtUtc = lastFailedAtUtc;
        NextAttemptNotBeforeUtc = nextAttemptNotBeforeUtc;
        LastResult = lastResult;
        ErrorSummary = errorSummary;
    }

    public string CertificateId { get; }
    public bool Enabled { get; }
    public IReadOnlyList<string> Domains { get; }
    public bool Active { get; }
    public string Source { get; }
    public DateTimeOffset? NotBeforeUtc { get; }
    public DateTimeOffset? NotAfterUtc { get; }
    public DateTimeOffset? RenewalDueAtUtc { get; }
    public DateTimeOffset? LastAttemptAtUtc { get; }
    public DateTimeOffset? LastSucceededAtUtc { get; }
    public DateTimeOffset? LastFailedAtUtc { get; }
    public DateTimeOffset? NextAttemptNotBeforeUtc { get; }
    public string LastResult { get; }
    public string? ErrorSummary { get; }
}
