using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.DAL.Registry;

public sealed class SqliteRegistryRepository : IRegistryRepository, IAsyncDisposable
{
    private const int MaximumStateBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };
    private readonly SqliteConnection _connection;
    private readonly FileStream _writerLock;
    private readonly string _siteId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _fence;

    private SqliteRegistryRepository(SqliteConnection connection, FileStream writerLock, string siteId)
    {
        _connection = connection;
        _writerLock = writerLock;
        _siteId = siteId;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The async factory transfers ownership to its successful caller. Its finally disposes failed repository initialization and every connection/lock not transferred; initialization failure and second-writer tests verify lock release.")]
    public static async ValueTask<SqliteRegistryRepository> OpenAsync(string stateDirectory, string siteId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(stateDirectory);
        RegistryNames.RequireLabel(siteId);
        ValidateDirectory(stateDirectory);
        var databasePath = Path.Combine(stateDirectory, "registry.sqlite");
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm", Path.Combine(stateDirectory, "registry-writer.lock") })
            if (new FileInfo(path).LinkTarget is not null) throw new InvalidDataException("Registry files cannot be symbolic links.");
        FileStream? writerLock = new(Path.Combine(stateDirectory, "registry-writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        SqliteConnection? connection = null;
        SqliteRegistryRepository? repository = null;
        try
        {
            connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            repository = new SqliteRegistryRepository(connection, writerLock, siteId);
            connection = null;
            writerLock = null;
            await repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(databasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var result = repository;
            repository = null;
            return result;
        }
        finally
        {
            if (repository is not null) await repository.DisposeAsync().ConfigureAwait(false);
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            if (writerLock is not null) await writerLock.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<RegistryState> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT revision, state, digest, fence, schema_version, site_id FROM registry_state WHERE id = 1";
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using var readerLifetime = reader.ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.GetInt64(3) != _fence || reader.GetInt32(4) != 1 || !string.Equals(reader.GetString(5), _siteId, StringComparison.Ordinal))
                throw new InvalidDataException("Registry writer fence or schema is invalid.");
            var bytes = (byte[])reader.GetValue(1);
            var digest = (byte[])reader.GetValue(2);
            if (bytes.Length > MaximumStateBytes || digest.Length != 32 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), digest))
                throw new InvalidDataException("Registry state integrity failed.");
            var state = JsonSerializer.Deserialize<RegistryState>(bytes, Json) ?? throw new InvalidDataException("Registry state is missing.");
            if (state.Revision != reader.GetInt64(0)) throw new InvalidDataException("Registry revision does not match its durable boundary.");
            return state;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask CommitAsync(long expectedRevision, RegistryState replacement, RegistryAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(audit);
        if (replacement.Revision != checked(expectedRevision + 1)) throw new InvalidDataException("Registry commit must advance one revision.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(replacement, Json);
        if (bytes.Length > MaximumStateBytes) throw new InvalidDataException("Durable registry capacity exceeded.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var transactionLifetime = transaction.ConfigureAwait(false);
            using var update = _connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE registry_state SET revision=$next, state=$state, digest=$digest WHERE id=1 AND revision=$previous AND fence=$fence AND schema_version=1 AND site_id=$site";
            update.Parameters.AddWithValue("$next", replacement.Revision);
            update.Parameters.AddWithValue("$state", bytes);
            update.Parameters.AddWithValue("$digest", SHA256.HashData(bytes));
            update.Parameters.AddWithValue("$previous", expectedRevision);
            update.Parameters.AddWithValue("$fence", _fence);
            update.Parameters.AddWithValue("$site", _siteId);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Registry revision conflict or writer fence was lost.");
            using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO registry_audit(revision, at_utc, operation, actor, subject) VALUES($revision,$at,$operation,$actor,$subject)";
            insert.Parameters.AddWithValue("$revision", replacement.Revision);
            insert.Parameters.AddWithValue("$at", audit.AtUtc.ToUnixTimeMilliseconds());
            insert.Parameters.AddWithValue("$operation", audit.Operation);
            insert.Parameters.AddWithValue("$actor", audit.Actor);
            insert.Parameters.AddWithValue("$subject", audit.Subject);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        await _writerLock.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            PRAGMA busy_timeout=2000;
            CREATE TABLE IF NOT EXISTS registry_state(id INTEGER PRIMARY KEY CHECK(id=1), schema_version INTEGER NOT NULL,
                revision INTEGER NOT NULL, fence INTEGER NOT NULL, state BLOB NOT NULL, digest BLOB NOT NULL, site_id TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS registry_audit(revision INTEGER PRIMARY KEY, at_utc INTEGER NOT NULL,
                operation TEXT NOT NULL, actor TEXT NOT NULL, subject TEXT NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        using var initial = _connection.CreateCommand();
        initial.Transaction = transaction;
        initial.CommandText = "INSERT OR IGNORE INTO registry_state VALUES(1,1,0,0,$state,$digest,$site)";
        var empty = JsonSerializer.SerializeToUtf8Bytes(RegistryState.Empty, Json);
        initial.Parameters.AddWithValue("$state", empty);
        initial.Parameters.AddWithValue("$digest", SHA256.HashData(empty));
        initial.Parameters.AddWithValue("$site", _siteId);
        await initial.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        using var fence = _connection.CreateCommand();
        fence.Transaction = transaction;
        fence.CommandText = "UPDATE registry_state SET fence=fence+1 WHERE id=1 AND schema_version=1 AND site_id=$site AND fence < 9223372036854775807 RETURNING fence";
        fence.Parameters.AddWithValue("$site", _siteId);
        _fence = (long)(await fence.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Unsupported registry schema or fence exhaustion."));
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory) || new DirectoryInfo(directory).LinkTarget is not null)
            throw new InvalidDataException("Registry requires a private absolute state directory.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(directory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != UnixFileMode.None)
            throw new InvalidDataException("Registry state directory must be private to its service account.");
    }
}
