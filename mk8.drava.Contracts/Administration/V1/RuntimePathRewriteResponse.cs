namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimePathRewriteResponse(string StripPrefix, string ReplacePrefix, string Replacement)
{
}
