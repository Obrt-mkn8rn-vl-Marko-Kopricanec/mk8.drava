namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimePathRewritePolicy
{
    public RuntimePathRewritePolicy(string StripPrefix, string ReplacePrefix, string Replacement)
    {
        RuntimePathRewriteFacts.Validate(StripPrefix, ReplacePrefix, Replacement);
        this.StripPrefix = StripPrefix;
        this.ReplacePrefix = ReplacePrefix;
        this.Replacement = Replacement;
    }

    public string StripPrefix { get; }
    public string ReplacePrefix { get; }
    public string Replacement { get; }
}
