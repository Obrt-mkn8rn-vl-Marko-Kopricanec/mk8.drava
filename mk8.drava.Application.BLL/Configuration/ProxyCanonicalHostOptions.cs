namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyCanonicalHostOptions
{
    public bool? Enabled { get; init; }
    public string TargetHost { get; init; } = "";
    public int? StatusCode { get; init; }
}
