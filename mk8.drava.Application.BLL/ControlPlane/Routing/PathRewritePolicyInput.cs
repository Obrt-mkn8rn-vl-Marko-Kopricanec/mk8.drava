namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed record PathRewritePolicyInput
{
    public PathRewritePolicyInput(string? StripPrefix, string? ReplacePrefix, string? Replacement)
    {
        this.StripPrefix = StripPrefix ?? "";
        this.ReplacePrefix = ReplacePrefix ?? "";
        this.Replacement = Replacement ?? "";
    }

    public string StripPrefix { get; }
    public string ReplacePrefix { get; }
    public string Replacement { get; }
}
