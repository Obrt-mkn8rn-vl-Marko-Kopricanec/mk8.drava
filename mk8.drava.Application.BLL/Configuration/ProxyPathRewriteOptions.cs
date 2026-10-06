namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyPathRewriteOptions
{
    public string StripPrefix { get; init; } = "";
    public string ReplacePrefix { get; init; } = "";
    public string Replacement { get; init; } = "";
}
