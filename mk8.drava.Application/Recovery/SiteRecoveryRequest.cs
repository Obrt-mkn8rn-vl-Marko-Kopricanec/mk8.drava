using Mk8.Drava.Application.BLL.ControlPlane.Backup;

namespace Mk8.Drava.Application.Recovery;

internal sealed record SiteRecoveryRequest
{
    public int SchemaVersion { get; init; }
    public string SourceDirectory { get; init; } = "";
    public string GatewayDirectory { get; init; } = "";
    public string ApplicationCollectionPath { get; init; } = "";
    public string DestinationDirectory { get; init; } = "";
    public string SiteId { get; init; } = "";
    public long MaximumRegistryBytes { get; init; }
    public long MaximumFileBytes { get; init; }
    public bool AllowCredentialEscrow { get; init; }
    public string ExpectedManifestSha256 { get; init; } = "";
    public RecoveryFileInput[] Files { get; init; } = [];
    public RecoveryFileApproval[] ApprovedFiles { get; init; } = [];
}
