namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyHttpsRedirectOptions
{
    public bool? Enabled { get; init; }
    public int? StatusCode { get; init; }
    public int? HttpsPort { get; init; }
}
