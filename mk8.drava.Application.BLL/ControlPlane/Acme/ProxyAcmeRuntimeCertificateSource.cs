using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record ProxyAcmeRuntimeCertificateSource
{
    public ProxyAcmeRuntimeCertificateSource(string Key, string Id, string Source, DateTimeOffset NotBeforeUtc, DateTimeOffset NotAfterUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        this.Key = Key;
        this.Id = Id;
        this.Source = Source;
        this.NotBeforeUtc = NotBeforeUtc;
        this.NotAfterUtc = NotAfterUtc;
    }

    public string Key { get; }
    public string Id { get; }
    public string Source { get; }
    public DateTimeOffset NotBeforeUtc { get; }
    public DateTimeOffset NotAfterUtc { get; }
}
