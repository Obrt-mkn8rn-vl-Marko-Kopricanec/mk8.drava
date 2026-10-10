namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeCertificateIssueRequest
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "The ACME command retains raw directory text and its required-string guards; issuer policy compares this text to the configured authority, so parsing or canonicalizing this member would change the input and matching contract.")]
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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "The ACME command retains raw directory text and its required-string guards; issuer policy compares this text to the configured authority, so parsing or canonicalizing this member would change the input and matching contract.")]
    public string DirectoryUrl { get; }
    public IReadOnlyList<string> ContactEmails { get; }
    public bool TermsAccepted { get; }
}
