namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyAdminAuditEventResponse(DateTimeOffset TimestampUtc, string Method, string Path, string? ClientIp, string AuthResult, int StatusCode, bool Succeeded)
{
}
