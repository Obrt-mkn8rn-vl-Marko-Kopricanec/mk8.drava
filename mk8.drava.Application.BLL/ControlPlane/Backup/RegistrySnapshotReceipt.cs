namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;

// A private registry image, not an issuer/key/configuration escrow or a serving-readiness receipt.
public sealed record RegistrySnapshotReceipt(string SiteId, long RegistryRevision, long PolicyRevision,
    long DatabaseBytes, string DatabaseSha256);
