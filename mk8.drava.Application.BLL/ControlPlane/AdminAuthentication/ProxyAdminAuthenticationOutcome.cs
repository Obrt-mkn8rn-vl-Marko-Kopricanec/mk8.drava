using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using System.Net;

namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
public sealed record ProxyAdminAuthenticationOutcome(bool Allowed, string AuthResult, int RecentAuditCapacity, bool ShouldChallenge, int? DeniedStatusCode);
