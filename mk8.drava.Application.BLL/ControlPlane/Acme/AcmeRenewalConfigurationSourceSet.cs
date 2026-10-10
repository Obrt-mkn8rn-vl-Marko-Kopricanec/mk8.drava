using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeRenewalConfigurationSourceSet
{
    public AcmeRenewalConfigurationSourceSet(bool Enabled, string StoragePath, string DirectoryUrl, IEnumerable<string> ContactEmails, bool TermsAccepted, int RetryAfterMinutes, IEnumerable<AcmeRenewalCertificateSource> Certificates)
    {
        ArgumentNullException.ThrowIfNull(Certificates);
        ArgumentException.ThrowIfNullOrWhiteSpace(StoragePath);
        ArgumentNullException.ThrowIfNull(DirectoryUrl);
        if (Enabled) ArgumentException.ThrowIfNullOrWhiteSpace(DirectoryUrl);
        this.Enabled = Enabled;
        this.StoragePath = StoragePath;
        this.DirectoryUrl = DirectoryUrl;
        this.ContactEmails = AcmeCommandFacts.CopyStrings(ContactEmails, nameof(ContactEmails));
        this.TermsAccepted = TermsAccepted;
        this.RetryAfterMinutes = RetryAfterMinutes;
        this.Certificates = AcmeList.Copy(Certificates.Select(RequireCertificate));
    }

    public bool Enabled { get; }
    public string StoragePath { get; }
    public string DirectoryUrl { get; }
    public IReadOnlyList<string> ContactEmails { get; }
    public bool TermsAccepted { get; }
    public int RetryAfterMinutes { get; }
    public IReadOnlyList<AcmeRenewalCertificateSource> Certificates { get; }

    private static AcmeRenewalCertificateSource RequireCertificate(AcmeRenewalCertificateSource certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate;
    }
}
