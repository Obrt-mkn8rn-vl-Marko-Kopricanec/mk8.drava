using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;

namespace Mk8.Drava.Application.DAL.Registry;

public sealed partial class SqliteRegistryRepository
{
    private const int MaximumAcmeStatusBytes = 4096;
    private static readonly string[] AcmeStatusProperties = ["certificateId", "enabled", "domains", "active", "source", "notBeforeUtc", "notAfterUtc",
        "renewalDueAtUtc", "lastAttemptAtUtc", "lastSucceededAtUtc", "lastFailedAtUtc", "nextAttemptNotBeforeUtc", "lastResult", "errorSummary"];

    internal async ValueTask<AcmeCertificateLifecycleStatus?> ReadAcmeStatusAsync(string scope, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RequireWriterFenceAsync(cancellationToken).ConfigureAwait(false);
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT schema_version, site_id, scope, state, digest FROM acme_lifecycle WHERE id=1";
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using var readerLifetime = reader.ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            if (reader.GetInt32(0) != 1 || !string.Equals(reader.GetString(1), _siteId, StringComparison.Ordinal) || !string.Equals(reader.GetString(2), scope, StringComparison.Ordinal))
                throw new InvalidDataException("ACME history schema, site or owner authority does not match.");
            var bytes = (byte[])reader.GetValue(3); var digest = (byte[])reader.GetValue(4);
            if (bytes.Length is < 1 or > MaximumAcmeStatusBytes || digest.Length != 32 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), digest))
                throw new InvalidDataException("ACME history integrity failed.");
            RequireAcmeJsonShape(bytes);
            return JsonSerializer.Deserialize<AcmeCertificateLifecycleStatus>(bytes, Json) ?? throw new InvalidDataException("ACME history is absent.");
        }
        finally { _gate.Release(); }
    }

    internal async ValueTask UpsertAcmeStatusAsync(string scope, AcmeCertificateLifecycleStatus status, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(status, Json);
        if (bytes.Length is < 1 or > MaximumAcmeStatusBytes) throw new InvalidDataException("ACME history exceeds its bounded state size.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var transactionLifetime = transaction.ConfigureAwait(false);
            using var command = _connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO acme_lifecycle(id,schema_version,site_id,scope,state,digest)
                SELECT 1,1,$site,$scope,$state,$digest FROM registry_state WHERE id=1 AND fence=$fence AND schema_version=1 AND site_id=$site
                ON CONFLICT(id) DO UPDATE SET state=excluded.state,digest=excluded.digest
                WHERE acme_lifecycle.schema_version=1 AND acme_lifecycle.site_id=excluded.site_id AND acme_lifecycle.scope=excluded.scope
                """;
            command.Parameters.AddWithValue("$site", _siteId); command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$state", bytes); command.Parameters.AddWithValue("$digest", SHA256.HashData(bytes));
            command.Parameters.AddWithValue("$fence", _fence);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("ACME history authority changed or its controller writer fence was lost.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async ValueTask RequireWriterFenceAsync(CancellationToken cancellationToken)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT fence FROM registry_state WHERE id=1 AND schema_version=1 AND site_id=$site";
        command.Parameters.AddWithValue("$site", _siteId);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not long fence || fence != _fence)
            throw new InvalidDataException("ACME history lost the controller writer fence.");
    }

    private static void RequireAcmeJsonShape(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("ACME history requires one status object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name) || !AcmeStatusProperties.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidDataException("ACME history properties are duplicated or unknown.");
        if (names.Count != AcmeStatusProperties.Length) throw new InvalidDataException("ACME history lacks required status fields.");
    }
}
