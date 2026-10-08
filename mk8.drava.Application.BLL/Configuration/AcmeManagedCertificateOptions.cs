namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class AcmeManagedCertificateOptions
{
    public string Id { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public IList<string> Domains { get; init; } = [];
    public int? RenewBeforeDays { get; init; }
}
