namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeCertificateProjection
{
    public RuntimeCertificateProjection(string Id, string Path, string Format, string Source, IReadOnlyList<string> Domains, bool HasConfiguredPassword, string? Subject, string? Thumbprint, DateTime NotBefore, DateTime NotAfter)
    {
        RuntimeCertificateFacts.ValidateProjection(Id, Path, Format, Source, NotBefore, NotAfter);
        this.Id = Id;
        this.Path = Path;
        this.Format = Format;
        this.Source = Source;
        this.Domains = RuntimeList.Copy(Domains);
        this.HasConfiguredPassword = HasConfiguredPassword;
        this.Subject = Subject;
        this.Thumbprint = Thumbprint;
        this.NotBefore = NotBefore;
        this.NotAfter = NotAfter;
    }

    public string Id { get; }
    public string Path { get; }
    public string Format { get; }
    public string Source { get; }
    public IReadOnlyList<string> Domains { get; }
    public bool HasConfiguredPassword { get; }
    public string? Subject { get; }
    public string? Thumbprint { get; }
    public DateTime NotBefore { get; }
    public DateTime NotAfter { get; }
}
