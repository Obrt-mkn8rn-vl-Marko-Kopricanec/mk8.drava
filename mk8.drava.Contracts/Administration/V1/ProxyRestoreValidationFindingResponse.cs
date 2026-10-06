namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyRestoreValidationFindingResponse(string Severity, string Code, string Message, string? RelativePath)
{
}
