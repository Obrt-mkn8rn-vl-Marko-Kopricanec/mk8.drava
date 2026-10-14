using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Installation;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.DAL.Storage;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.DAL.Registry;

public static partial class RegistrySnapshot
{
    // Only the typed policy collection gets this bound. Ordinary recovery inputs retain their 8MiB limit.
    private const int MaximumPolicyCollectionBytes = 32 * 1024 * 1024;

    public static ValueTask<RegistrySnapshotReceipt> CreateProtectedAsync(string sourceDirectory, string destinationDirectory,
        string siteId, long maximumBytes, string applicationCollectionPath, bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationCollectionPath);
        RequirePolicyApproval(allowCredentialEscrow);
        return CopyAsync(sourceDirectory, destinationDirectory, siteId, maximumBytes, expectedSha256: null,
            new PolicySnapshotInput(applicationCollectionPath, Expected: null, allowCredentialEscrow), cancellationToken);
    }

    public static ValueTask<RegistrySnapshotReceipt> RestoreProtectedAsync(string sourceDirectory, string destinationDirectory,
        string siteId, long maximumBytes, RegistrySnapshotReceipt expected, bool allowCredentialEscrow, CancellationToken cancellationToken)
        => RestoreProtectedCoreAsync(sourceDirectory, destinationDirectory, siteId, maximumBytes, expected, includeManual: false, allowCredentialEscrow, cancellationToken);

    public static ValueTask<RegistrySnapshotReceipt> CreateRuntimeAsync(string sourceDirectory, string destinationDirectory,
        string siteId, long maximumBytes, string applicationCollectionPath, bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationCollectionPath);
        RequirePolicyApproval(allowCredentialEscrow);
        return CopyAsync(sourceDirectory, destinationDirectory, siteId, maximumBytes, expectedSha256: null,
            new PolicySnapshotInput(applicationCollectionPath, Expected: null, allowCredentialEscrow, IncludeManual: true), cancellationToken);
    }

    public static ValueTask<RegistrySnapshotReceipt> RestoreRuntimeAsync(string sourceDirectory, string destinationDirectory,
        string siteId, long maximumBytes, RegistrySnapshotReceipt expected, bool allowCredentialEscrow, CancellationToken cancellationToken)
        => RestoreProtectedCoreAsync(sourceDirectory, destinationDirectory, siteId, maximumBytes, expected, includeManual: true, allowCredentialEscrow, cancellationToken);

    private static ValueTask<RegistrySnapshotReceipt> RestoreProtectedCoreAsync(string sourceDirectory, string destinationDirectory,
        string siteId, long maximumBytes, RegistrySnapshotReceipt expected, bool includeManual, bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        RequirePolicyApproval(allowCredentialEscrow);
        RequirePolicyReceipt(expected, siteId);
        var escrow = expected.PolicyEscrow ?? throw new InvalidDataException("Policy restore requires approved escrow.");
        if (escrow.SchemaVersion != (includeManual ? 2 : 1)) throw new InvalidDataException("Requested runtime inputs and approved escrow schema differ.");
        var approved = expected with { PolicyEscrow = escrow with { References = Array.AsReadOnly(escrow.References.ToArray()) } };
        return CopyAsync(sourceDirectory, destinationDirectory, siteId, maximumBytes, expected.DatabaseSha256,
            new PolicySnapshotInput(CollectionPath: null, approved, allowCredentialEscrow, includeManual), cancellationToken);
    }

    // The caller owns a private, unpublished workspace and its freshly protected Application role.
    public static async ValueTask<RegistrySnapshotReceipt> ValidatePreparedProtectedAsync(string directory, string siteId,
        long maximumBytes, PolicyPayloadSnapshot payloads, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        RegistryNames.RequireLabel(siteId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 4096);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, 1L << 40);
        PrivateOwnerFile.RequirePrivateDirectory(directory);
        await RequireReferenceImageAsync(directory, cancellationToken).ConfigureAwait(false);
        var validated = await ValidateImageAsync(directory, siteId, maximumBytes, new CapturedPolicyStore(payloads), closeAuthority: false, cancellationToken).ConfigureAwait(false);
        return validated.Receipt;
    }

    private static async ValueTask<CapturedPolicyStore> ReadPolicyInputAsync(string sourceDirectory, string siteId,
        PolicySnapshotInput input, CancellationToken cancellationToken)
    {
        RequirePolicyApproval(input.AllowCredentialEscrow);
        await RequireReferenceImageAsync(sourceDirectory, cancellationToken).ConfigureAwait(false);
        await RequireApprovedAuthorityAsync(sourceDirectory, siteId, input, cancellationToken).ConfigureAwait(false);
        PortablePolicyArchive archive;
        PortableManualConfigurationDocument? manual = null;
        if (input.CollectionPath is { } collectionPath)
        {
            if (input.IncludeManual)
            {
                var documents = await PortablePolicyEscrow.ReadApplicationRuntimeAsync(collectionPath, siteId, sourceDirectory,
                    input.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false);
                archive = documents.PolicyArchive;
                manual = documents.ManualConfiguration;
            }
            else
            {
                archive = await PortablePolicyEscrow.ReadApplicationArchiveAsync(collectionPath, siteId, sourceDirectory,
                input.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            var expected = input.Expected ?? throw new InvalidDataException("Policy restore requires an approved receipt.");
            var escrow = expected.PolicyEscrow ?? throw new InvalidDataException("Approved policy escrow is absent.");
            var environment = Path.Combine(sourceDirectory, "policy");
            RequirePolicyInventory(environment);
            await RequirePolicyFileAsync(Path.Combine(environment, ".env"), escrow.ArchiveBytes, escrow.ArchiveSha256, MaximumPolicyCollectionBytes, cancellationToken).ConfigureAwait(false);
            await RequirePolicyFileAsync(Path.Combine(environment, ".installation.key"), escrow.KeyBytes, escrow.KeySha256, 64, cancellationToken).ConfigureAwait(false);
            var database = new FileInfo(Path.Combine(sourceDirectory, "registry.sqlite"));
            if (database.Length != expected.DatabaseBytes) throw new InvalidDataException("Protected registry image length differs from its approved receipt.");
            if (input.IncludeManual)
            {
                var documents = await PortablePolicyEscrow.LoadRuntimeAsync(environment, siteId, input.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false);
                archive = documents.PolicyArchive;
                manual = documents.ManualConfiguration;
                if (DescribeManual(manual) != escrow.ManualConfiguration) throw new InvalidDataException("Protected manual input differs from its approved receipt.");
            }
            else
            {
                archive = await PortablePolicyEscrow.LoadAsync(environment, siteId, input.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false);
            }
        }
        if (manual is not null) PortableManualConfigurationSource.ValidateDocument(manual, siteId);
        return new CapturedPolicyStore(PortablePolicyArchiveCodec.Read(archive, siteId), manual);
    }

    private static async Task RequireApprovedAuthorityAsync(string sourceDirectory, string siteId,
        PolicySnapshotInput input, CancellationToken cancellationToken)
    {
        var connection = Connection(Path.Combine(sourceDirectory, "registry.sqlite"), SqliteOpenMode.ReadOnly);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = connection.CreateCommand();
            await using var commandLifetime = command.ConfigureAwait(false);
            if (input.Expected is { } expected)
            {
                RequirePolicyReceipt(expected, siteId);
                command.CommandText = "SELECT r.revision,p.revision FROM registry_state r JOIN policy_state p ON p.id=1 WHERE r.id=1";
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using var readerLifetime = reader.ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.GetInt64(0) != expected.RegistryRevision || reader.GetInt64(1) != expected.PolicyRevision)
                    throw new InvalidDataException("Protected registry image authority differs from its approved receipt.");
            }
        }
    }

    private static async Task RequireReferenceImageAsync(string directory, CancellationToken cancellationToken)
    {
        var connection = Connection(Path.Combine(directory, "registry.sqlite"), SqliteOpenMode.ReadOnly);
        await using var lifetime = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        command.CommandText = "SELECT schema_version FROM policy_state WHERE id=1";
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L) != 2L)
            throw new InvalidDataException("Protected recovery requires reference policy schema.");
        await PolicyReferenceSchema.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
        command.CommandText = "SELECT cleanup_complete FROM policy_state WHERE id=1";
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L) != 1L)
            throw new InvalidDataException("Protected recovery requires completed plaintext cleanup.");
    }

    private static async ValueTask<PolicyEscrowReceipt> ExportPolicyAsync(PrivateSiteWorkspace workspace, string siteId,
        CapturedPolicyStore store, IReadOnlyList<PolicyRevision> history, CancellationToken cancellationToken)
    {
        var payloads = new Dictionary<string, string>(StringComparer.Ordinal);
        var references = history.Select(static revision => PolicyPayloadSnapshot.Describe(revision.CanonicalJson)).ToArray();
        foreach (var reference in references) payloads.TryAdd(reference.Digest, store.Snapshot.Resolve(reference));
        var archive = PortablePolicyArchiveCodec.Encode(new PolicyPayloadSnapshot(siteId, payloads));
        var environment = workspace.CreateDirectory("policy");
        if (store.ManualConfiguration is { } manual)
            await PortablePolicyEscrow.ExportRuntimeAsync(environment, siteId, new PortableRuntimeDocuments(archive, manual), allowCredentialEscrow: true, cancellationToken).ConfigureAwait(false);
        else await PortablePolicyEscrow.ExportAsync(environment, siteId, archive, allowCredentialEscrow: true, cancellationToken).ConfigureAwait(false);
        var (collectionBytes, collectionSha256) = await DescribePolicyFileAsync(Path.Combine(environment, ".env"), cancellationToken).ConfigureAwait(false);
        var (keyBytes, keySha256) = await DescribePolicyFileAsync(Path.Combine(environment, ".installation.key"), cancellationToken).ConfigureAwait(false);
        return new PolicyEscrowReceipt(store.ManualConfiguration is null ? 1 : 2, collectionBytes, collectionSha256, keyBytes, keySha256, Array.AsReadOnly(references))
        { ManualConfiguration = store.ManualConfiguration is { } input ? DescribeManual(input) : null };
    }

    private static void RequirePolicyReceipt(RegistrySnapshotReceipt expected, string siteId)
    {
        if (!string.Equals(expected.SiteId, siteId, StringComparison.Ordinal) || expected.RegistryRevision < 0 || expected.PolicyRevision < 0 || expected.DatabaseBytes < 4096)
            throw new InvalidDataException("Protected registry receipt authority is invalid.");
        RegistryNames.RequireFingerprint(expected.DatabaseSha256);
        var policy = expected.PolicyEscrow;
        if (policy is null || policy.SchemaVersion is not (1 or 2) || policy.ArchiveBytes is < 1 or > MaximumPolicyCollectionBytes
            || policy.KeyBytes is < 32 or > 64 || policy.References is null || policy.References.Count != Math.Min(expected.PolicyRevision, 64))
        {
            throw new InvalidDataException("Protected policy receipt schema or bounds are invalid.");
        }

        RegistryNames.RequireFingerprint(policy.ArchiveSha256);
        RegistryNames.RequireFingerprint(policy.KeySha256);
        if (policy.SchemaVersion == 1 ? policy.ManualConfiguration is not null : policy.ManualConfiguration is null)
            throw new InvalidDataException("Runtime recovery schema and manual input receipt differ.");
        if (policy.ManualConfiguration is { } manual)
        {
            if (manual.Utf8Bytes is < 0 or > 4 * 1024 * 1024) throw new InvalidDataException("Protected manual receipt exceeds its document bound.");
            RegistryNames.RequireFingerprint(manual.Sha256);
        }
        foreach (var reference in policy.References)
        {
            if (reference is null) throw new InvalidDataException("Protected policy receipt contains an absent reference.");
            _ = new PolicyPayloadReference(reference.Digest, reference.Utf8Bytes);
        }
    }

    private static void RequireApprovedReferences(RegistrySnapshotReceipt? expected, IReadOnlyList<PolicyRevision> history)
    {
        if (expected is null) return;
        var references = expected.PolicyEscrow?.References ?? throw new InvalidDataException("Policy restore has no approved reference inventory.");
        if (!references.SequenceEqual(history.Select(static revision => PolicyPayloadSnapshot.Describe(revision.CanonicalJson))))
            throw new InvalidDataException("Protected policy history differs from its approved ordered reference inventory.");
    }

    private static void RequirePolicyInventory(string environment)
    {
        PrivateOwnerFile.RequirePrivateDirectory(environment);
        var expected = new HashSet<string>([".env", ".installation.key"], StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(environment))
            if (!expected.Remove(Path.GetFileName(path))) throw new InvalidDataException("Policy escrow contains unexpected entries.");
        if (expected.Count != 0) throw new InvalidDataException("Policy escrow is missing an approved file.");
    }

    private static async Task RequirePolicyFileAsync(string path, long bytes, string sha256, long maximumBytes, CancellationToken cancellationToken)
    {
        var file = PrivateOwnerFile.OpenRead(path);
        await using var lifetime = file.ConfigureAwait(false);
        if (file.Length != bytes || file.Length < 1 || file.Length > maximumBytes) throw new InvalidDataException("Policy escrow file length differs from its approved bound.");
        if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)), sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Policy escrow file differs from its approved digest.");
    }

    private static async Task<(long Bytes, string Sha256)> DescribePolicyFileAsync(string path, CancellationToken cancellationToken)
    {
        var file = PrivateOwnerFile.OpenRead(path);
        await using var lifetime = file.ConfigureAwait(false);
        return (file.Length, Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)));
    }

    private static void RequirePolicyApproval(bool allowCredentialEscrow)
    {
        if (!allowCredentialEscrow) throw new InvalidOperationException("Protected policy recovery requires explicit credential escrow approval.");
    }

    private static ManualConfigurationReceipt DescribeManual(PortableManualConfigurationDocument document)
    {
        var bytes = Encoding.UTF8.GetBytes(document.Reveal());
        try { return new ManualConfigurationReceipt(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal sealed record PolicySnapshotInput(string? CollectionPath, RegistrySnapshotReceipt? Expected, bool AllowCredentialEscrow, bool IncludeManual = false);

    private sealed class CapturedPolicyStore(PolicyPayloadSnapshot snapshot, PortableManualConfigurationDocument? manual = null) : IPolicyPayloadStore
    {
        internal PolicyPayloadSnapshot Snapshot { get; } = snapshot;
        internal PortableManualConfigurationDocument? ManualConfiguration { get; } = manual;

        public ValueTask<PolicyPayloadSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Snapshot);
        }

        public ValueTask<PolicyPayloadReference> StageAsync(string canonicalJson, IReadOnlyList<PolicyPayloadReference> retained, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Recovery validation cannot stage or migrate policy payloads.");
    }
}
