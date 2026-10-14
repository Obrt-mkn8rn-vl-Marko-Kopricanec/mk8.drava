namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;

// Policy escrow is explicit; this never contains issuer or service credentials or proves serving readiness.
public sealed record RegistrySnapshotReceipt(string SiteId, long RegistryRevision, long PolicyRevision,
    long DatabaseBytes, string DatabaseSha256)
{
    public PolicyEscrowReceipt? PolicyEscrow { get; init; }
}
