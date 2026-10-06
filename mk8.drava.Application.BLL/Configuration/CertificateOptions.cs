namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class CertificateOptions
{
    public string Id { get; init; } = "";
    public string Format { get; init; } = "pfx";
    public string Path { get; init; } = "";
    public string? Password { get; init; }
    public string? PasswordEnvironmentVariable { get; init; }
}
