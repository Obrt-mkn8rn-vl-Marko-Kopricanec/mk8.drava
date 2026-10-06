namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
public sealed record ProxyAdminAuditEvent(DateTimeOffset TimestampUtc, string Method, string Path, string? ClientIp, string AuthResult, int StatusCode, bool Succeeded);
