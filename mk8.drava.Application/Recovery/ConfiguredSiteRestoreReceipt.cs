using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.BLL.NoConf;

namespace Mk8.Drava.Application.Recovery;

internal sealed record ConfiguredSiteRestoreReceipt(int SchemaVersion, string SourceManifestSha256, RegistrySnapshotReceipt Registry,
    IReadOnlyList<RecoveryFileReceipt> Files, IReadOnlyList<RestoredFilePlacement> Placements,
    string ApplicationBootstrapSha256, string GatewayBootstrapSha256)
{
    public IReadOnlyList<PolicyPayloadReference>? PolicyReferences { get; init; }
    public ManualConfigurationReceipt? ManualConfiguration { get; init; }
}
