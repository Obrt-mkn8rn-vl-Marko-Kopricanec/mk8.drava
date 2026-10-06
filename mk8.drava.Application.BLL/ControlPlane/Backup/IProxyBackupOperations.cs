namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public interface IProxyBackupOperations
{
    ProxyBackupManifest CreateManifest();
    ValueTask<ProxyRestoreValidationResult> ValidateAsync(CancellationToken cancellationToken);
}
