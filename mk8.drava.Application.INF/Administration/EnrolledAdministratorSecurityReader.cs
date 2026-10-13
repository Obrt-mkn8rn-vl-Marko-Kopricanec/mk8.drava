using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.Administration;

public sealed class EnrolledAdministratorSecurityReader : IProxyAdminSecurityOptionsReader
{
    private readonly string? _token;

    public EnrolledAdministratorSecurityReader(SecretText token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _token = token.IsEmpty ? null : token.RequireToken();
    }

    public ProxyAdminSecurityOptionsReadResult Read() => ProxyAdminSecurityOptionsReadResult.FromActiveConfiguration(requireAuthentication: true, _token, recentAuditCapacity: 1024);
}
