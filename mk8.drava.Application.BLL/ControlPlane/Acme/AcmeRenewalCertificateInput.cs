using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeRenewalCertificateInput
{
    public AcmeRenewalCertificateInput(string Id, bool Enabled, IEnumerable<string> Domains, int RenewBeforeDays, AcmeRenewalActiveCertificate? ActiveCertificate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
        this.Enabled = Enabled;
        this.Domains = AcmeCommandFacts.CopyRequiredStrings(Domains, nameof(Domains));
        this.RenewBeforeDays = RenewBeforeDays;
        this.ActiveCertificate = ActiveCertificate;
    }

    public string Id { get; }
    public bool Enabled { get; }
    public IReadOnlyList<string> Domains { get; }
    public int RenewBeforeDays { get; }
    public AcmeRenewalActiveCertificate? ActiveCertificate { get; }
}
