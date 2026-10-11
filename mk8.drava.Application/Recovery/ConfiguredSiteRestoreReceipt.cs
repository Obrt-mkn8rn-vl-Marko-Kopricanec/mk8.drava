using Mk8.Drava.Application.BLL.ControlPlane.Backup;

namespace Mk8.Drava.Application.Recovery;

internal sealed record ConfiguredSiteRestoreReceipt(int SchemaVersion, string SourceManifestSha256, RegistrySnapshotReceipt Registry,
    IReadOnlyList<RecoveryFileReceipt> Files, IReadOnlyList<RestoredFilePlacement> Placements,
    string ApplicationBootstrapSha256, string GatewayBootstrapSha256);
