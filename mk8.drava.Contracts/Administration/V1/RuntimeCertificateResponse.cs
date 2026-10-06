namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeCertificateResponse
{
    public RuntimeCertificateResponse(string id, string path, string format, string source, IReadOnlyList<string> domains, bool hasConfiguredPassword, string? subject, string? thumbprint, DateTime notBefore, DateTime notAfter)
    {
        Id = id;
        Path = path;
        Format = format;
        Source = source;
        Domains = ApiResponseList.Copy(domains);
        HasConfiguredPassword = hasConfiguredPassword;
        Subject = subject;
        Thumbprint = thumbprint;
        NotBefore = notBefore;
        NotAfter = notAfter;
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
