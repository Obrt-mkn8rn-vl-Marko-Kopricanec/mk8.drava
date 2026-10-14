using Microsoft.Data.Sqlite;

namespace Mk8.Drava.Application.DAL.Registry;

// The protected schema cannot retain a plaintext payload column or executable policy-schema extensions.
internal static class PolicyReferenceSchema
{
    // Read existing authority before initialization can run a caller-added trigger or advance the fence.
    internal static async Task RequireExistingAsync(SqliteConnection connection, bool protectedStorage, CancellationToken cancellationToken)
    {
        await RequireNoExtensionsAsync(connection, cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using var lifetime = command.ConfigureAwait(false);
        command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name='policy_state'";
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? -1L) == 0) return;
        command.CommandText = "SELECT schema_version FROM policy_state WHERE id=1";
        var schema = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (schema is not long version || version is not (1 or 2)) throw new InvalidDataException("Policy storage schema is unsupported.");
        if (version == 1) return;
        if (!protectedStorage) throw new InvalidDataException("Reference policy storage requires its protected archive.");
        await RequireAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task RequireAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await RequireColumnsAsync(connection, state: true,
            [("id", "INTEGER", 0, 1), ("schema_version", "INTEGER", 1, 0), ("revision", "INTEGER", 1, 0), ("cleanup_complete", "INTEGER", 1, 0)], cancellationToken).ConfigureAwait(false);
        await RequireColumnsAsync(connection, state: false,
            [("revision", "INTEGER", 0, 1), ("payload_digest", "BLOB", 1, 0), ("payload_bytes", "INTEGER", 1, 0),
             ("at_utc", "INTEGER", 1, 0), ("actor", "TEXT", 1, 0), ("source", "TEXT", 1, 0)], cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using var lifetime = command.ConfigureAwait(false);
        await RequireNoExtensionsAsync(connection, cancellationToken).ConfigureAwait(false);
        command.CommandText = "SELECT COUNT(*) FROM policy_state WHERE id!=1 OR typeof(schema_version)!='integer' OR schema_version!=2 OR typeof(revision)!='integer' OR revision<0 OR typeof(cleanup_complete)!='integer' OR cleanup_complete NOT IN (0,1)";
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? -1L) != 0)
            throw new InvalidDataException("Protected policy authority has invalid storage types or values.");
        command.CommandText = "SELECT COUNT(*) FROM policy_state";
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? -1L) != 1)
            throw new InvalidDataException("Protected policy authority requires exactly one state row.");
        command.CommandText = """
            SELECT COUNT(*) FROM policy_revisions
            WHERE typeof(revision)!='integer' OR revision<1
               OR typeof(payload_digest)!='blob' OR length(payload_digest)!=32
               OR typeof(payload_bytes)!='integer' OR payload_bytes NOT BETWEEN 2 AND 262144
               OR typeof(at_utc)!='integer' OR at_utc NOT BETWEEN -62135596800000 AND 253402300799999
               OR typeof(actor)!='text' OR typeof(source)!='text'
            """;
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? -1L) != 0)
            throw new InvalidDataException("Protected policy references have invalid storage types or values.");
    }

    private static async Task RequireNoExtensionsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using var lifetime = command.ConfigureAwait(false);
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_schema
            WHERE type IN ('trigger','view') OR (type='table' AND name='policy_revisions_legacy')
            """;
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? -1L) != 0)
            throw new InvalidDataException("Protected policy schema contains unsupported executable or plaintext extensions.");
    }

    private static async Task RequireColumnsAsync(SqliteConnection connection, bool state,
        IReadOnlyList<(string Name, string Type, int NotNull, int PrimaryKey)> expected, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        if (state) command.CommandText = "SELECT name,type,\"notnull\",pk,hidden FROM pragma_table_xinfo('policy_state') ORDER BY cid";
        else command.CommandText = "SELECT name,type,\"notnull\",pk,hidden FROM pragma_table_xinfo('policy_revisions') ORDER BY cid";
        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using var readerLifetime = reader.ConfigureAwait(false);
        var index = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (index >= expected.Count) throw new InvalidDataException("Protected policy schema contains extra payload or authority columns.");
            var column = expected[index++];
            if (!string.Equals(reader.GetString(0), column.Name, StringComparison.Ordinal)
                || !string.Equals(reader.GetString(1), column.Type, StringComparison.Ordinal)
                || reader.GetInt32(2) != column.NotNull || reader.GetInt32(3) != column.PrimaryKey || reader.GetInt32(4) != 0)
            {
                throw new InvalidDataException("Protected policy schema differs from its reference-only contract.");
            }
        }
        if (index != expected.Count) throw new InvalidDataException("Protected policy schema is incomplete.");
    }
}
