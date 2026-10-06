namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record ProxyConfigurationFileDiscovery
{
    public ProxyConfigurationFileDiscovery(string Path, string Format, string Status, string? Reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        ArgumentException.ThrowIfNullOrWhiteSpace(Format);
        ArgumentException.ThrowIfNullOrWhiteSpace(Status);
        if (Reason is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Reason);
        }

        this.Path = Path;
        this.Format = Format;
        this.Status = Status;
        this.Reason = Reason;
    }

    public string Path { get; }
    public string Format { get; }
    public string Status { get; }
    public string? Reason { get; }
}
