namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeCertificateLifecycleStatus
{
    public AcmeCertificateLifecycleStatus(string CertificateId, bool Enabled, IReadOnlyList<string> Domains, bool Active, string Source, DateTimeOffset? NotBeforeUtc, DateTimeOffset? NotAfterUtc, DateTimeOffset? RenewalDueAtUtc, DateTimeOffset? LastAttemptAtUtc, DateTimeOffset? LastSucceededAtUtc, DateTimeOffset? LastFailedAtUtc, DateTimeOffset? NextAttemptNotBeforeUtc, string LastResult, string? ErrorSummary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CertificateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(LastResult);
        this.CertificateId = CertificateId;
        this.Enabled = Enabled;
        this.Domains = AcmeCommandFacts.CopyRequiredStrings(Domains, nameof(Domains));
        this.Active = Active;
        this.Source = Source;
        this.NotBeforeUtc = NotBeforeUtc;
        this.NotAfterUtc = NotAfterUtc;
        this.RenewalDueAtUtc = RenewalDueAtUtc;
        this.LastAttemptAtUtc = LastAttemptAtUtc;
        this.LastSucceededAtUtc = LastSucceededAtUtc;
        this.LastFailedAtUtc = LastFailedAtUtc;
        this.NextAttemptNotBeforeUtc = NextAttemptNotBeforeUtc;
        this.LastResult = LastResult;
        this.ErrorSummary = ErrorSummary;
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

    public static AcmeCertificateLifecycleStatus FromConfiguredCertificate(ProxyAcmeConfiguredCertificateStatus certificate, AcmeCertificateLifecycleActiveCertificate? activeCertificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (activeCertificate is null)
        {
            return new AcmeCertificateLifecycleStatus(certificate.Id, certificate.Enabled, certificate.Domains, false, "none", null, null, null, LastAttemptAtUtc: null, LastSucceededAtUtc: null, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, "inactive", ErrorSummary: null);
        }

        return new AcmeCertificateLifecycleStatus(certificate.Id, certificate.Enabled, certificate.Domains, true, "acme", activeCertificate.NotBeforeUtc, activeCertificate.NotAfterUtc, activeCertificate.NotAfterUtc.AddDays(-certificate.RenewBeforeDays), LastAttemptAtUtc: null, LastSucceededAtUtc: null, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, "loaded", ErrorSummary: null);
    }
}
