using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Installation;
using Mk8.Drava.Application.DAL.Storage;

namespace Mk8.Drava.Application.DAL.Registry;

// Explicit inventory only: no directory crawling, implicit key export, path rewriting or activation.
public static class SiteRecoveryBundle
{
    private const int MaximumFiles = 128;
    private const int MaximumFileBytes = 8 * 1024 * 1024;
    private const int MaximumManifestBytes = 65536;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    public static ValueTask<SiteRecoveryResult> CreateAsync(string applicationDirectory, string gatewayDirectory,
        string destinationDirectory, string siteId, long maximumRegistryBytes, long maximumFileBytes,
        IReadOnlyList<RecoveryFileInput> files, bool allowCredentialEscrow, CancellationToken cancellationToken) =>
        CreateCoreAsync(applicationDirectory, gatewayDirectory, destinationDirectory, siteId, maximumRegistryBytes, maximumFileBytes,
            files, applicationCollectionPath: null, allowCredentialEscrow, cancellationToken);

    public static ValueTask<SiteRecoveryResult> CreateProtectedAsync(string applicationDirectory, string gatewayDirectory,
        string destinationDirectory, string siteId, long maximumRegistryBytes, long maximumFileBytes,
        IReadOnlyList<RecoveryFileInput> files, string applicationCollectionPath, bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationCollectionPath);
        if (!allowCredentialEscrow) throw new InvalidOperationException("Policy recovery requires explicit credential escrow approval.");
        return CreateCoreAsync(applicationDirectory, gatewayDirectory, destinationDirectory, siteId, maximumRegistryBytes, maximumFileBytes,
            files, applicationCollectionPath, allowCredentialEscrow, cancellationToken);
    }

    private static async ValueTask<SiteRecoveryResult> CreateCoreAsync(string applicationDirectory, string gatewayDirectory,
        string destinationDirectory, string siteId, long maximumRegistryBytes, long maximumFileBytes,
        IReadOnlyList<RecoveryFileInput> files, string? applicationCollectionPath, bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count > MaximumFiles || (files.Count == 0 && applicationCollectionPath is null))
            throw new InvalidDataException("Recovery requires a bounded explicit inventory; only protected policy recovery may omit ordinary files.");
        RecoveryFileInput[] inventory = [.. files];
        if (inventory.Any(static file => file is null)) throw new InvalidDataException("Missing recovery input.");
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInventory([.. inventory.Select(static file => new RecoveryFileApproval(file.Name, file.Credential))], allowCredentialEscrow, maximumFileBytes);
        PrivateOwnerFile.RequirePrivateDirectory(applicationDirectory);
        PrivateOwnerFile.RequirePrivateDirectory(gatewayDirectory);
        RequireDestinationParent(destinationDirectory);
        using var workspace = PrivateSiteWorkspace.Create(destinationDirectory);
        var applicationLease = PrivateOwnerFile.AcquireLease(Path.Combine(applicationDirectory, "application.lock"));
        await using var applicationLeaseLifetime = applicationLease.ConfigureAwait(false);
        var gatewayLease = PrivateOwnerFile.AcquireLease(Path.Combine(gatewayDirectory, "gateway.lock"));
        await using var gatewayLeaseLifetime = gatewayLease.ConfigureAwait(false);
        var registryLease = PrivateOwnerFile.AcquireLease(Path.Combine(applicationDirectory, "registry-writer.lock"));
        await using var registryLeaseLifetime = registryLease.ConfigureAwait(false);
        var registry = await RegistrySnapshot.CopyWithWritersStoppedAsync(applicationDirectory,
            Path.Combine(workspace.StagingDirectory, "registry"), siteId, maximumRegistryBytes, expectedSha256: null,
            applicationCollectionPath is null ? null : new RegistrySnapshot.PolicySnapshotInput(applicationCollectionPath, Expected: null, allowCredentialEscrow), cancellationToken).ConfigureAwait(false);
        workspace.CreateDirectory("files");
        var receipts = new List<RecoveryFileReceipt>(inventory.Length);
        long total = 0;
        foreach (var input in inventory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receipt = await CopyFileAsync(input.SourcePath, Path.Combine(workspace.StagingDirectory, "files", input.Name),
                input.Name, input.Credential, maximumFileBytes - total, expected: null, cancellationToken).ConfigureAwait(false);
            total = checked(total + receipt.Bytes);
            receipts.Add(receipt);
        }
        return await PublishAsync(workspace, destinationDirectory, new SiteRecoveryReceipt(applicationCollectionPath is null ? 1 : 2, registry, receipts), cancellationToken).ConfigureAwait(false);
    }

    public static ValueTask<SiteRecoveryResult> RestoreAsync(string sourceDirectory, string destinationDirectory,
        string siteId, string expectedManifestSha256, long maximumRegistryBytes, long maximumFileBytes,
        IReadOnlyList<RecoveryFileApproval> approvedFiles, bool allowCredentialEscrow, CancellationToken cancellationToken) =>
        RestoreCoreAsync(sourceDirectory, destinationDirectory, siteId, expectedManifestSha256, maximumRegistryBytes, maximumFileBytes,
            approvedFiles, allowCredentialEscrow, expectedSchemaVersion: null, cancellationToken);

    public static ValueTask<SiteRecoveryResult> RestoreVersionedAsync(string sourceDirectory, string destinationDirectory,
        string siteId, string expectedManifestSha256, long maximumRegistryBytes, long maximumFileBytes,
        IReadOnlyList<RecoveryFileApproval> approvedFiles, bool allowCredentialEscrow, int expectedSchemaVersion, CancellationToken cancellationToken)
    {
        if (expectedSchemaVersion is not (1 or 2)) throw new InvalidDataException("Unsupported approved recovery schema.");
        return RestoreCoreAsync(sourceDirectory, destinationDirectory, siteId, expectedManifestSha256, maximumRegistryBytes, maximumFileBytes,
            approvedFiles, allowCredentialEscrow, expectedSchemaVersion, cancellationToken);
    }

    private static async ValueTask<SiteRecoveryResult> RestoreCoreAsync(string sourceDirectory, string destinationDirectory,
        string siteId, string expectedManifestSha256, long maximumRegistryBytes, long maximumFileBytes,
        IReadOnlyList<RecoveryFileApproval> approvedFiles, bool allowCredentialEscrow, int? expectedSchemaVersion, CancellationToken cancellationToken)
    {
        RegistryNames.RequireFingerprint(expectedManifestSha256);
        ValidateInventory(approvedFiles, allowCredentialEscrow, maximumFileBytes);
        RecoveryFileApproval[] approvals = [.. approvedFiles];
        ValidateInventory(approvals, allowCredentialEscrow, maximumFileBytes);
        PrivateOwnerFile.RequirePrivateDirectory(sourceDirectory);
        var sourceLease = PrivateOwnerFile.AcquireLease(Path.Combine(sourceDirectory, "recovery.lock"));
        await using var sourceLeaseLifetime = sourceLease.ConfigureAwait(false);
        var bytes = PrivateOwnerFile.Read(Path.Combine(sourceDirectory, "manifest.json"), 1, MaximumManifestBytes);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Recovery manifest differs from the caller's expected digest.");
        var manifest = JsonSerializer.Deserialize<SiteRecoveryReceipt>(bytes, JsonOptions) ?? throw new InvalidDataException("Missing recovery manifest.");
        if (expectedSchemaVersion is { } expected && manifest.SchemaVersion != expected)
            throw new InvalidDataException("Requested and recovered site schema differ.");
        ValidateManifest(manifest, siteId, approvals, allowCredentialEscrow);
        RequireExactInventory(sourceDirectory, manifest);
        RequireDestinationParent(destinationDirectory);
        using var workspace = PrivateSiteWorkspace.Create(destinationDirectory);
        var registry = manifest.SchemaVersion == 2
            ? await RegistrySnapshot.RestoreProtectedAsync(Path.Combine(sourceDirectory, "registry"),
                Path.Combine(workspace.StagingDirectory, "registry"), siteId, maximumRegistryBytes, manifest.Registry, allowCredentialEscrow, cancellationToken).ConfigureAwait(false)
            : await RegistrySnapshot.RestoreAsync(Path.Combine(sourceDirectory, "registry"),
                Path.Combine(workspace.StagingDirectory, "registry"), siteId, maximumRegistryBytes, manifest.Registry.DatabaseSha256, cancellationToken).ConfigureAwait(false);
        workspace.CreateDirectory("files");
        long total = 0;
        foreach (var entry in manifest.Files)
        {
            var copied = await CopyFileAsync(Path.Combine(sourceDirectory, "files", entry.Name),
                Path.Combine(workspace.StagingDirectory, "files", entry.Name), entry.Name, entry.Credential,
                maximumFileBytes - total, entry, cancellationToken).ConfigureAwait(false);
            total = checked(total + copied.Bytes);
        }
        return await PublishAsync(workspace, destinationDirectory, manifest with { Registry = registry }, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateInventory(IReadOnlyList<RecoveryFileApproval> files, bool allowCredentialEscrow, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, (long)MaximumFiles * MaximumFileBytes);
        if (files.Count > MaximumFiles) throw new InvalidDataException("Recovery requires a bounded explicit file inventory.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (file is null || string.IsNullOrEmpty(file.Name) || file.Name.Length > 128 ||
                file.Name[0] == '.' || file.Name.Any(static character => character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '-' and not '_' and not '.') ||
                !names.Add(file.Name))
            {
                throw new InvalidDataException("Recovery file names must be unique portable leaf names.");
            }
            var extension = Path.GetExtension(file.Name);
            if (!file.Credential && (extension.Equals(".token", StringComparison.OrdinalIgnoreCase) || extension.Equals(".key", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Known credential file types require credential classification.");
            }
            if (file.Credential && !allowCredentialEscrow) throw new InvalidDataException("Credential escrow requires explicit caller approval.");
        }
    }

    private static void ValidateManifest(SiteRecoveryReceipt manifest, string siteId, RecoveryFileApproval[] approvals, bool allowCredentialEscrow)
    {
        if (manifest.SchemaVersion is not (1 or 2) || manifest.Registry is null || manifest.Files is null ||
            !string.Equals(manifest.Registry.SiteId, siteId, StringComparison.Ordinal) || manifest.Files.Count != approvals.Length)
        {
            throw new InvalidDataException("Recovery manifest identity or inventory differs from caller approval.");
        }
        if (manifest.SchemaVersion == 1 ? manifest.Registry.PolicyEscrow is not null || manifest.Files.Count == 0 : manifest.Registry.PolicyEscrow is null || !allowCredentialEscrow)
            throw new InvalidDataException("Recovery schema and policy escrow approval do not agree.");
        RegistryNames.RequireFingerprint(manifest.Registry.DatabaseSha256);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files)
        {
            if (entry is null || !seen.Add(entry.Name) || entry.Bytes is < 1 or > MaximumFileBytes ||
                !approvals.Any(approval => string.Equals(approval.Name, entry.Name, StringComparison.Ordinal) && approval.Credential == entry.Credential))
            {
                throw new InvalidDataException("Recovery file was not explicitly approved.");
            }
            RegistryNames.RequireFingerprint(entry.Sha256);
        }
    }

    private static void RequireExactInventory(string source, SiteRecoveryReceipt manifest)
    {
        PrivateOwnerFile.RequirePrivateDirectory(Path.Combine(source, "files"));
        var expected = new HashSet<string>(manifest.Files.Select(static file => file.Name), StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(Path.Combine(source, "files")))
            if (!expected.Remove(Path.GetFileName(entry))) throw new InvalidDataException("Recovery contains an unexpected file.");
        if (expected.Count != 0) throw new InvalidDataException("Recovery is missing an approved file.");
        PrivateOwnerFile.RequirePrivateDirectory(Path.Combine(source, "registry"));
        var registryNames = new HashSet<string>(["registry.sqlite", "application.lock", "registry-writer.lock"], StringComparer.Ordinal);
        if (manifest.SchemaVersion == 2) registryNames.Add("policy");
        foreach (var entry in Directory.EnumerateFileSystemEntries(Path.Combine(source, "registry")))
            if (!registryNames.Remove(Path.GetFileName(entry))) throw new InvalidDataException("Recovery registry contains an unexpected entry.");
        var rootNames = new HashSet<string>(["registry", "files", "manifest.json", "recovery.lock"], StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            if (!rootNames.Remove(Path.GetFileName(entry))) throw new InvalidDataException("Recovery contains an unexpected root entry.");
    }

    private static void RequireDestinationParent(string destination)
    {
        PrivateOwnerFile.RequirePrivateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Missing recovery parent."));
    }

    private static async ValueTask<RecoveryFileReceipt> CopyFileAsync(string source, string destination, string name, bool credential,
        long remainingBytes, RecoveryFileReceipt? expected, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(source) || !string.Equals(Path.GetFullPath(source), source, StringComparison.Ordinal))
            throw new InvalidDataException("Recovery inputs require canonical absolute file paths.");
        var input = PrivateOwnerFile.OpenRead(source);
        await using var inputLifetime = input.ConfigureAwait(false);
        if (input.Length < 1 || input.Length > Math.Min(MaximumFileBytes, remainingBytes) || (expected is not null && input.Length != expected.Bytes))
            throw new InvalidDataException("Recovery file exceeds its approved byte bound.");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var output = new FileStream(destination, options);
        await using var outputLifetime = output.ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long total = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            total = checked(total + count);
            if (total > Math.Min(MaximumFileBytes, remainingBytes)) throw new InvalidDataException("Recovery file grew beyond its approved bound.");
            hash.AppendData(buffer.AsSpan(0, count));
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        var digest = Convert.ToHexString(hash.GetHashAndReset());
        if (total != input.Length || (expected is not null && (total != expected.Bytes || !string.Equals(digest, expected.Sha256, StringComparison.Ordinal))))
            throw new InvalidDataException("Recovery file differs from its declared length or digest.");
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        RandomAccess.FlushToDisk(output.SafeFileHandle);
        return new RecoveryFileReceipt(name, credential, total, digest);
    }

    private static async ValueTask<SiteRecoveryResult> PublishAsync(PrivateSiteWorkspace workspace, string destination,
        SiteRecoveryReceipt receipt, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions);
        if (bytes.Length > MaximumManifestBytes) throw new InvalidDataException("Recovery manifest exceeds its bound.");
        await workspace.WriteAsync("manifest.json", bytes, cancellationToken).ConfigureAwait(false);
        var manifest = PrivateOwnerFile.AcquireLease(Path.Combine(workspace.StagingDirectory, "manifest.json"));
        await using (manifest.ConfigureAwait(false))
        {
            RandomAccess.FlushToDisk(manifest.SafeFileHandle);
        }
        PrivateOwnerFile.SynchronizeDirectory(Path.Combine(workspace.StagingDirectory, "files"));
        PrivateOwnerFile.SynchronizeDirectory(workspace.StagingDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        workspace.Publish();
        try { PrivateOwnerFile.SynchronizeDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Missing recovery parent.")); }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            throw new IOException("Recovery bundle was published but synchronization failed; inspect before retrying.", exception);
        }
        return new SiteRecoveryResult(receipt, Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
