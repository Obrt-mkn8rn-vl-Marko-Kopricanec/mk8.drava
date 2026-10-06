namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeAcmeCertificateProjection
{
    public RuntimeAcmeCertificateProjection(string Id, bool Enabled, IReadOnlyList<string> Domains, int RenewBeforeDays)
    {
        ArgumentNullException.ThrowIfNull(Id);
        this.Id = Id;
        this.Enabled = Enabled;
        this.Domains = RuntimeList.Copy(Domains);
        this.RenewBeforeDays = RenewBeforeDays;
    }

    public string Id { get; }
    public bool Enabled { get; }
    public IReadOnlyList<string> Domains { get; }
    public int RenewBeforeDays { get; }
}
