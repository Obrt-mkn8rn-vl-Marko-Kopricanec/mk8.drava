namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;

public sealed record RecoveryFileInput(string SourcePath, string Name, bool Credential);
