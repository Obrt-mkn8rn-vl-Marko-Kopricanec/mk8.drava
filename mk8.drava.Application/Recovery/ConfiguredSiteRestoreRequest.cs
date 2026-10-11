using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Recovery;

internal sealed record ConfiguredSiteRestoreRequest
{
    public int SchemaVersion { get; init; }
    public string SourceDirectory { get; init; } = "";
    public string DestinationDirectory { get; init; } = "";
    public string ExpectedManifestSha256 { get; init; } = "";
    public long MaximumRegistryBytes { get; init; }
    public long MaximumFileBytes { get; init; }
    public bool AllowCredentialEscrow { get; init; }
    public ApplicationBootstrap Application { get; init; } = new();
    public GatewayBootstrap Gateway { get; init; } = new();
    public RecoveryFileApproval[] ApprovedFiles { get; init; } = [];
    public RestoredFilePlacement[] Placements { get; init; } = [];
}
