using Mk8.Drava.Application.BLL.NoConf;

namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;

// References are ordered newest first, one per SQL-live revision, including repeated digests.
public sealed record PolicyEscrowReceipt(int SchemaVersion, long ArchiveBytes, string ArchiveSha256,
    long KeyBytes, string KeySha256, IReadOnlyList<PolicyPayloadReference> References)
{
    public ManualConfigurationReceipt? ManualConfiguration { get; init; }
}
