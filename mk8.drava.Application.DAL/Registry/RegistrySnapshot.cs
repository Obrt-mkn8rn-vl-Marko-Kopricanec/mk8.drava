using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Installation;
using Mk8.Drava.Application.DAL.Storage;

namespace Mk8.Drava.Application.DAL.Registry;

public static class RegistrySnapshot
{
    public static ValueTask<RegistrySnapshotReceipt> CreateAsync(string sourceDirectory, string destinationDirectory,
        string siteId, long maximumBytes, CancellationToken cancellationToken) =>
        CopyAsync(sourceDirectory, destinationDirectory, siteId, maximumBytes, expectedSha256: null, cancellationToken);

    public static ValueTask<RegistrySnapshotReceipt> RestoreAsync(string sourceDirectory, string destinationDirectory,
        string siteId, long maximumBytes, string expectedSha256, CancellationToken cancellationToken)
    {
        RegistryNames.RequireFingerprint(expectedSha256);
        return CopyAsync(sourceDirectory, destinationDirectory, siteId, maximumBytes, expectedSha256, cancellationToken);
    }

    private static async ValueTask<RegistrySnapshotReceipt> CopyAsync(string sourceDirectory, string destinationDirectory,
        string siteId, long maximumBytes, string? expectedSha256, CancellationToken cancellationToken)
    {
        RegistryNames.RequireLabel(siteId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 4096);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, 1L << 40);
        cancellationToken.ThrowIfCancellationRequested();
        PrivateOwnerFile.RequirePrivateDirectory(sourceDirectory);
        var applicationLease = PrivateOwnerFile.AcquireLease(Path.Combine(sourceDirectory, "application.lock"));
        await using var applicationLifetime = applicationLease.ConfigureAwait(false);
        var registryLease = PrivateOwnerFile.AcquireLease(Path.Combine(sourceDirectory, "registry-writer.lock"));
        await using var registryLifetime = registryLease.ConfigureAwait(false);
        return await CopyWithWritersStoppedAsync(sourceDirectory, destinationDirectory, siteId, maximumBytes, expectedSha256, cancellationToken).ConfigureAwait(false);
    }

    // Both callers retain Application and registry writer leases through this operation.
    internal static async ValueTask<RegistrySnapshotReceipt> CopyWithWritersStoppedAsync(string sourceDirectory, string destinationDirectory,
        string siteId, long maximumBytes, string? expectedSha256, CancellationToken cancellationToken)
    {
        RegistryNames.RequireLabel(siteId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 4096);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, 1L << 40);
        cancellationToken.ThrowIfCancellationRequested();
        PrivateOwnerFile.RequirePrivateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "registry.sqlite");
        await using var sourceFile = PrivateOwnerFile.OpenRead(sourcePath).ConfigureAwait(false);
        RequireSidecars(sourcePath, maximumBytes, expectedSha256 is not null);
        if (expectedSha256 is not null) await RequireDigestAsync(sourcePath, maximumBytes, expectedSha256, cancellationToken).ConfigureAwait(false);
        using var workspace = PrivateSiteWorkspace.Create(destinationDirectory);
        PrivateOwnerFile.RequirePrivateDirectory(Path.GetDirectoryName(destinationDirectory) ?? throw new InvalidDataException("Missing snapshot parent."));
        var targetPath = Path.Combine(workspace.StagingDirectory, "registry.sqlite");
        await CopyDatabaseAsync(sourcePath, targetPath, siteId, maximumBytes, cancellationToken).ConfigureAwait(false);
        var receipt = await ValidateImageAsync(workspace.StagingDirectory, siteId, maximumBytes,
            closeAuthority: expectedSha256 is not null, cancellationToken).ConfigureAwait(false);
        // Publication has one commit point. Cancellation after rename cannot retract a published image.
        cancellationToken.ThrowIfCancellationRequested();
        PrivateOwnerFile.SynchronizeDirectory(workspace.StagingDirectory);
        Publish(workspace, destinationDirectory);
        return receipt;
    }

    private static void Publish(PrivateSiteWorkspace workspace, string destinationDirectory)
    {
        workspace.Publish();
        try
        {
            PrivateOwnerFile.SynchronizeDirectory(Path.GetDirectoryName(destinationDirectory) ?? throw new InvalidDataException("Missing snapshot parent."));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            throw new IOException("Registry image was published but directory synchronization failed; inspect the destination before retrying.", exception);
        }
    }

    private static void RequireSidecars(string path, long maximumBytes, bool restoring)
    {
        long total = 0;
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var candidate = path + suffix;
            if (new FileInfo(candidate).LinkTarget is not null) throw new InvalidDataException("Registry recovery refuses symbolic files.");
            if (!File.Exists(candidate)) continue;
            using var file = PrivateOwnerFile.OpenRead(candidate);
            if (restoring && suffix.Length > 0) throw new InvalidDataException("A registry snapshot must be a standalone image without sidecars.");
            total = checked(total + file.Length);
            if (total > maximumBytes) throw new InvalidDataException("Registry source exceeds the configured snapshot byte bound.");
        }
    }

    private static async ValueTask CopyDatabaseAsync(string sourcePath, string targetPath, string siteId,
        long maximumBytes, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var file = new FileStream(targetPath, options);
        await using (file.ConfigureAwait(false)) { }
        var source = Connection(sourcePath, SqliteOpenMode.ReadOnly);
        await using var sourceLifetime = source.ConfigureAwait(false);
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RequireSchemaAsync(source, siteId, cancellationToken).ConfigureAwait(false);
        var target = Connection(targetPath, SqliteOpenMode.ReadWrite);
        await using var targetLifetime = target.ConfigureAwait(false);
        await target.OpenAsync(cancellationToken).ConfigureAwait(false);
        CopyPages(source, target, maximumBytes, cancellationToken);
        var command = target.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        command.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void CopyPages(SqliteConnection source, SqliteConnection target, long maximumBytes, CancellationToken cancellationToken)
    {
        var sourceHandle = source.Handle ?? throw new InvalidOperationException("Snapshot source is closed.");
        var targetHandle = target.Handle ?? throw new InvalidOperationException("Snapshot destination is closed.");
        using var backup = SQLitePCL.raw.sqlite3_backup_init(targetHandle, "main", sourceHandle, "main");
        if (backup.IsInvalid) throw new InvalidDataException("SQLite could not initialize the registry snapshot.");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = SQLitePCL.raw.sqlite3_backup_step(backup, 64);
            if (result is not (SQLitePCL.raw.SQLITE_OK or SQLitePCL.raw.SQLITE_DONE))
                throw new IOException("SQLite registry snapshot failed; no destination was published.");
            if (new FileInfo(target.DataSource).Length > maximumBytes) throw new InvalidDataException("Registry snapshot exceeds its configured byte bound.");
            if (result == SQLitePCL.raw.SQLITE_DONE) return;
        }
    }

    private static async ValueTask RequireSchemaAsync(SqliteConnection connection, string siteId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name IN
            ('registry_state','registry_audit','policy_state','policy_revisions','acme_lifecycle')
            """;
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L) != 5)
            throw new InvalidDataException("Registry snapshot is missing required schema tables.");
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_schema WHERE type IN ('table','view','trigger')
            AND name NOT IN ('registry_state','registry_audit','policy_state','policy_revisions','acme_lifecycle','dns_mutation_journal')
            AND name NOT GLOB 'sqlite_*'
            """;
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L) != 0)
            throw new InvalidDataException("Registry snapshot contains an unsupported schema extension.");
        command.CommandText = "SELECT schema_version, site_id FROM registry_state WHERE id=1";
        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using var readerLifetime = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.GetInt32(0) != 1 || !string.Equals(reader.GetString(1), siteId, StringComparison.Ordinal))
            throw new InvalidDataException("Registry snapshot schema or site authority differs from the caller.");
    }

    private static async ValueTask<RegistrySnapshotReceipt> ValidateImageAsync(string directory, string siteId, long maximumBytes,
        bool closeAuthority, CancellationToken cancellationToken)
    {
        long revision;
        long policyRevision;
        var repository = await SqliteRegistryRepository.OpenAsync(directory, siteId, cancellationToken).ConfigureAwait(false);
        await using (repository.ConfigureAwait(false))
        {
            var state = await repository.ReadAsync(cancellationToken).ConfigureAwait(false);
            revision = state.Revision;
            policyRevision = (await repository.ReadPolicyAsync(cancellationToken).ConfigureAwait(false))?.Revision ?? 0;
            await repository.ReadPolicyHistoryAsync(cancellationToken).ConfigureAwait(false);
            await repository.ValidateBackupJournalsAsync(revision, cancellationToken).ConfigureAwait(false);
            if (closeAuthority)
            {
                var closed = RegistryRestoreAuthorityPolicy.CloseImportedAuthority(state);
                await repository.CommitAsync(revision, closed,
                    new RegistryAudit(DateTimeOffset.UtcNow, "restore-authority-reset", "registry-recovery", siteId), cancellationToken).ConfigureAwait(false);
                revision = closed.Revision;
            }
        }
        var path = Path.Combine(directory, "registry.sqlite");
        await SealImageAsync(path, cancellationToken).ConfigureAwait(false);
        RequireSidecars(path, maximumBytes, restoring: true);
        var file = PrivateOwnerFile.AcquireLease(path);
        await using var fileLifetime = file.ConfigureAwait(false);
        if (file.Length > maximumBytes) throw new InvalidDataException("Validated snapshot exceeds its configured byte bound.");
        RandomAccess.FlushToDisk(file.SafeFileHandle);
        file.Position = 0;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        File.Delete(Path.Combine(directory, "registry-writer.lock"));
        return new RegistrySnapshotReceipt(siteId, revision, policyRevision, file.Length, digest);
    }

    private static async ValueTask SealImageAsync(string path, CancellationToken cancellationToken)
    {
        var connection = Connection(path, SqliteOpenMode.ReadWrite);
        await using var lifetime = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        command.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = "PRAGMA integrity_check";
        if (!string.Equals((string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Registry snapshot failed SQLite integrity validation.");
    }

    private static async ValueTask RequireDigestAsync(string path, long maximumBytes, string expected, CancellationToken cancellationToken)
    {
        var file = PrivateOwnerFile.OpenRead(path);
        await using var lifetime = file.ConfigureAwait(false);
        if (file.Length > maximumBytes) throw new InvalidDataException("Snapshot exceeds its configured byte bound.");
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(digest, expected, StringComparison.Ordinal)) throw new InvalidDataException("Snapshot differs from the caller's expected digest.");
    }

    private static SqliteConnection Connection(string path, SqliteOpenMode mode) =>
        new(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 2 }.ToString());
}
