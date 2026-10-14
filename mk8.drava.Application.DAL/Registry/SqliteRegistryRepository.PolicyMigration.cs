using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.Storage;

namespace Mk8.Drava.Application.DAL.Registry;

public sealed partial class SqliteRegistryRepository
{
    private async Task InitializePolicyStorageAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        command.CommandText = "SELECT schema_version FROM policy_state WHERE id=1";
        _policySchema = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        if (_policySchema == 1 && _policyPayloads is not null) await MigratePolicyReferencesAsync(cancellationToken).ConfigureAwait(false);
        if (_policySchema == 1) return;
        if (_policySchema != 2) throw new InvalidDataException("Policy storage schema is unsupported.");
        await PolicyReferenceSchema.RequireAsync(_connection, cancellationToken).ConfigureAwait(false);
        _ = RequirePayloadStore();
        await ReadReferenceInventoryAsync(transaction: null, requireCleanupComplete: false, cancellationToken).ConfigureAwait(false);
        command.CommandText = "SELECT cleanup_complete FROM policy_state WHERE id=1 AND schema_version=2";
        var complete = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? -1L);
        if (complete is not (0 or 1)) throw new InvalidDataException("Policy storage cleanup state is invalid.");
        if (complete == 0) await CompletePolicyCleanupAsync(cancellationToken).ConfigureAwait(false);
        await ReadReferenceInventoryAsync(transaction: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task MigratePolicyReferencesAsync(CancellationToken cancellationToken)
    {
        var state = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var history = await ReadLegacyPoliciesAsync(includeHistory: true, cancellationToken).ConfigureAwait(false);
        var check = _connection.CreateCommand();
        await using var checkLifetime = check.ConfigureAwait(false);
        check.CommandText = "SELECT COUNT(*) FROM policy_revisions";
        if ((long)(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? -1L) != history.Count)
            throw new InvalidDataException("Legacy policy history contains unreachable metadata.");
        check.CommandText = "PRAGMA secure_delete=ON";
        await check.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        var head = history.Count == 0 ? 0 : history[0].Revision;
        if (!await CheckPolicyAuthorityAsync(transaction, head, state.Revision, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Policy authority changed before protected migration.");
        var retained = new List<PolicyPayloadReference>(history.Count);
        foreach (var revision in history)
        {
            var reference = await RequirePayloadStore().StageAsync(revision.CanonicalJson, retained, cancellationToken).ConfigureAwait(false);
            if (reference != PolicyPayloadSnapshot.Describe(revision.CanonicalJson)) throw new InvalidDataException("Policy migration staging returned an invalid reference.");
            retained.Add(reference);
        }
        await ObservePolicyMigrationAsync(PolicyMigrationPhase.ArchiveStaged).ConfigureAwait(false);
        var migrate = _connection.CreateCommand();
        await using var migrateLifetime = migrate.ConfigureAwait(false);
        migrate.Transaction = transaction;
        migrate.CommandText = """
            ALTER TABLE policy_revisions RENAME TO policy_revisions_legacy;
            CREATE TABLE policy_revisions(revision INTEGER PRIMARY KEY,
                payload_digest BLOB NOT NULL CHECK(length(payload_digest)=32),
                payload_bytes INTEGER NOT NULL CHECK(payload_bytes BETWEEN 2 AND 262144),
                at_utc INTEGER NOT NULL,actor TEXT NOT NULL,source TEXT NOT NULL);
            INSERT INTO policy_revisions(revision,payload_digest,payload_bytes,at_utc,actor,source)
                SELECT revision,digest,length(canonical),at_utc,actor,source FROM policy_revisions_legacy;
            DROP TABLE policy_revisions_legacy;
            ALTER TABLE policy_state ADD COLUMN cleanup_complete INTEGER NOT NULL DEFAULT 0;
            UPDATE policy_state SET schema_version=2,cleanup_complete=0 WHERE id=1 AND schema_version=1;
            """;
        await migrate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await ObservePolicyMigrationAsync(PolicyMigrationPhase.SchemaPrepared).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        _policySchema = 2;
        await ObservePolicyMigrationAsync(PolicyMigrationPhase.SchemaCommitted).ConfigureAwait(false);
    }

    private async Task CompletePolicyCleanupAsync(CancellationToken cancellationToken)
    {
        await RequirePolicyCheckpointAsync(cancellationToken).ConfigureAwait(false);
        await ObservePolicyMigrationAsync(PolicyMigrationPhase.InitialCheckpointCompleted).ConfigureAwait(false);
        var command = _connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        command.CommandText = "VACUUM";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await RequirePolicyCheckpointAsync(cancellationToken).ConfigureAwait(false);
        SynchronizePolicyDatabase();
        await ObservePolicyMigrationAsync(PolicyMigrationPhase.CleanupSynchronized).ConfigureAwait(false);
        command.CommandText = "UPDATE policy_state SET cleanup_complete=1 WHERE id=1 AND schema_version=2 AND cleanup_complete=0";
        if (await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false) != 1)
            throw new InvalidDataException("Policy cleanup completion state changed unexpectedly.");
        await RequirePolicyCheckpointAsync(CancellationToken.None).ConfigureAwait(false);
        SynchronizePolicyDatabase();
    }

    private ValueTask ObservePolicyMigrationAsync(PolicyMigrationPhase phase) =>
        _policyMigrationObserver is { } observer ? observer(phase) : ValueTask.CompletedTask;

    private async Task RequirePolicyCheckpointAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using var readerLifetime = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.GetInt32(0) != 0)
            throw new IOException("Policy plaintext cleanup could not truncate the active WAL.");
    }

    private void SynchronizePolicyDatabase()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Protected policy migration requires the qualified Linux durability profile.");
        var path = _connection.DataSource;
        using var database = PrivateOwnerFile.OpenRead(path);
        RandomAccess.FlushToDisk(database.SafeFileHandle);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (!File.Exists(path + suffix)) continue;
            using var sidecar = PrivateOwnerFile.OpenRead(path + suffix);
            RandomAccess.FlushToDisk(sidecar.SafeFileHandle);
        }
        PrivateOwnerFile.SynchronizeDirectory(Path.GetDirectoryName(path) ?? throw new InvalidDataException("Policy database parent is missing."));
    }
}
