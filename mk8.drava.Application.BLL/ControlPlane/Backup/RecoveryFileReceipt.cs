namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;

public sealed record RecoveryFileReceipt(string Name, bool Credential, long Bytes, string Sha256);
