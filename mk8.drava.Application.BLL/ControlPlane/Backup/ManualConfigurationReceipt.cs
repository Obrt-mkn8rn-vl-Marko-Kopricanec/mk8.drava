namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;

// Binds the decrypted manual document in addition to the protected collection/key hashes.
public sealed record ManualConfigurationReceipt(int Utf8Bytes, string Sha256);
