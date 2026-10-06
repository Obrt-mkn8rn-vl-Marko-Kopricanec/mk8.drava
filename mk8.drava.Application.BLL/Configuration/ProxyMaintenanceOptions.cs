namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyMaintenanceOptions
{
    public bool? Enabled { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public string ContentType { get; init; } = "text/plain; charset=utf-8";
    public string Body { get; init; } = "Service Unavailable";
}
