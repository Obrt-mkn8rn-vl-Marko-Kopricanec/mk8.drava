namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed class ProxyBackupAdministrationService
{
    private readonly IProxyBackupOperations _backupOperations;
    public ProxyBackupAdministrationService(IProxyBackupOperations backupOperations)
    {
        _backupOperations = backupOperations;
    }

    public ProxyBackupManifest CreateManifest()
    {
        return _backupOperations.CreateManifest();
    }

    public ValueTask<ProxyRestoreValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        return _backupOperations.ValidateAsync(cancellationToken);
    }
}
