using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;

namespace Mk8.Drava.Application.INF.Runtime;
public sealed class ProxyAdminSecurityOptionsReader : IProxyAdminSecurityOptionsReader
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    public ProxyAdminSecurityOptionsReader(IProxyActiveConfigurationSnapshotReader configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public ProxyAdminSecurityOptionsReadResult Read()
    {
        if (_configurationStore.ReadSnapshot()is ProxyConfigurationSnapshotReadResult.AvailableResult available)
        {
            var snapshot = available.Snapshot;
            return ProxyAdminSecurityOptionsReadResult.FromActiveConfiguration(snapshot.AdminSecurity.RequireAuthentication, snapshot.AdminSecurity.Token, snapshot.AdminSecurity.RecentAuditCapacity);
        }

        var adminOptions = new ProxyAdminOptions();
        var security = ProxyConfigurationRuntimeMapper.ToRuntimeAdminSecurityOptions(adminOptions, ProxyAdminSecurityTokenPolicy.Resolve(adminOptions, Environment.GetEnvironmentVariable));
        return ProxyAdminSecurityOptionsReadResult.FromDefaults(security.RequireAuthentication, security.Token, security.RecentAuditCapacity);
    }
}
