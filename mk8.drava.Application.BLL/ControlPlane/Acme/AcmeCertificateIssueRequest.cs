namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeCertificateIssueRequest
{
    public AcmeCertificateIssueRequest(string CertificateId, IReadOnlyList<string> Domains, string DirectoryUrl, IReadOnlyList<string> ContactEmails, bool TermsAccepted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CertificateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DirectoryUrl);
        this.CertificateId = CertificateId;
        this.Domains = AcmeCommandFacts.CopyRequiredStrings(Domains, nameof(Domains));
        this.DirectoryUrl = DirectoryUrl;
        this.ContactEmails = AcmeCommandFacts.CopyStrings(ContactEmails, nameof(ContactEmails));
        this.TermsAccepted = TermsAccepted;
    }

    public string CertificateId { get; }
    public IReadOnlyList<string> Domains { get; }
    public string DirectoryUrl { get; }
    public IReadOnlyList<string> ContactEmails { get; }
    public bool TermsAccepted { get; }
}
