namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record ProxyAcmeConfiguredCertificateStatus
{
    public ProxyAcmeConfiguredCertificateStatus(string Id, bool Enabled, IEnumerable<string> Domains, int RenewBeforeDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
        this.Enabled = Enabled;
        this.Domains = AcmeCommandFacts.CopyRequiredStrings(Domains, nameof(Domains));
        this.RenewBeforeDays = RenewBeforeDays;
    }

    public string Id { get; }
    public bool Enabled { get; }
    public IReadOnlyList<string> Domains { get; }
    public int RenewBeforeDays { get; }
}
