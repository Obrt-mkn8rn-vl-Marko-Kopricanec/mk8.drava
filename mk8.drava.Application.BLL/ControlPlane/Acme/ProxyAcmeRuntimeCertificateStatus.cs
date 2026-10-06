namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record ProxyAcmeRuntimeCertificateStatus
{
    public ProxyAcmeRuntimeCertificateStatus(string Id, string Source, DateTimeOffset NotBeforeUtc, DateTimeOffset NotAfterUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        this.Id = Id;
        this.Source = Source;
        this.NotBeforeUtc = NotBeforeUtc;
        this.NotAfterUtc = NotAfterUtc;
    }

    public string Id { get; }
    public string Source { get; }
    public DateTimeOffset NotBeforeUtc { get; }
    public DateTimeOffset NotAfterUtc { get; }
}
