using System.Security.Cryptography.X509Certificates;

namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeCertificate
{
    public RuntimeCertificate(string Id, string Path, string Format, bool HasConfiguredPassword, X509Certificate2 Certificate, string Source, IReadOnlyList<string> Domains)
    {
        RuntimeCertificateFacts.Validate(Id, Path, Format, Source);
        ArgumentNullException.ThrowIfNull(Certificate);
        this.Id = Id;
        this.Path = Path;
        this.Format = Format;
        this.HasConfiguredPassword = HasConfiguredPassword;
        this.Certificate = Certificate;
        this.Source = Source;
        this.Domains = RuntimeList.Copy(Domains);
    }

    public string Id { get; }
    public string Path { get; }
    public string Format { get; }
    public bool HasConfiguredPassword { get; }
    public X509Certificate2 Certificate { get; }
    public string Source { get; }
    public IReadOnlyList<string> Domains { get; }
}
