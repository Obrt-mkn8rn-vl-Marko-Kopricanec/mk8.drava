using Mk8.Drava.Application.BLL.ControlPlane.Backup;

namespace Mk8.Drava.Application.Recovery;

internal sealed record RegistryRecoveryRequest
{
    public int SchemaVersion { get; init; }
    public string SourceDirectory { get; init; } = "";
    public string DestinationDirectory { get; init; } = "";
    public string SiteId { get; init; } = "";
    public long MaximumBytes { get; init; }
    public string ExpectedImageSha256 { get; init; } = "";
    public string ApplicationCollectionPath { get; init; } = "";
    public bool AllowCredentialEscrow { get; init; }
    public RegistrySnapshotReceipt? ExpectedProtectedImage { get; init; }
}
