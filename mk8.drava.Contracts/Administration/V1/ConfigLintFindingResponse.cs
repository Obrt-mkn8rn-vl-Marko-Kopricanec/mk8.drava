namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ConfigLintFindingResponse(string Severity, string Code, string Message, string? Source, string? Path, string? SuggestedFix)
{
}
