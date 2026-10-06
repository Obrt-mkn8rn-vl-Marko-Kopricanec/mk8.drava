using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Mk8.Drava.Application.DAL.Administration;

namespace Mk8.Drava.Application.INF.Administration;

public sealed class EnrolledAdministratorSecurityReader : IProxyAdminSecurityOptionsReader
{
    private readonly string? _token;

    public EnrolledAdministratorSecurityReader(string tokenPath)
    {
        ArgumentNullException.ThrowIfNull(tokenPath);
        _token = tokenPath.Length == 0 ? null : AdministratorCredentialFile.Read(tokenPath);
    }

    public ProxyAdminSecurityOptionsReadResult Read() => ProxyAdminSecurityOptionsReadResult.FromActiveConfiguration(requireAuthentication: true, _token, recentAuditCapacity: 1024);
}
