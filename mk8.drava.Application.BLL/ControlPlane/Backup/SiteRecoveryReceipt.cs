namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;

public sealed record SiteRecoveryReceipt(int SchemaVersion, RegistrySnapshotReceipt Registry, IReadOnlyList<RecoveryFileReceipt> Files);
