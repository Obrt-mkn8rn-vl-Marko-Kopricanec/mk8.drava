namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public sealed class ProxyConfigurationReloadAdministrationService<TProjection>
    where TProjection : class
{
    private readonly IProxyConfigurationReloadOperations<TProjection> _reloadOperations;
    public ProxyConfigurationReloadAdministrationService(IProxyConfigurationReloadOperations<TProjection> reloadOperations)
    {
        _reloadOperations = reloadOperations;
    }

    public ValueTask<ProxyConfigurationReloadResult<TProjection>> ReloadAsync(CancellationToken cancellationToken)
    {
        return _reloadOperations.ReloadAsync(cancellationToken);
    }
}
