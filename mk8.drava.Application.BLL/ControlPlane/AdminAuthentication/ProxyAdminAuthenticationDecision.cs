using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
public sealed record ProxyAdminAuthenticationDecision(bool Allowed, bool AuthenticationRequired, string Result, bool ShouldChallenge);
