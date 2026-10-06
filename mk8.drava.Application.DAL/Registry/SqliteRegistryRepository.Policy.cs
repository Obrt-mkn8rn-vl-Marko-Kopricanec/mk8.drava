using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.NoConf;

namespace Mk8.Drava.Application.DAL.Registry;

public sealed partial class SqliteRegistryRepository
{
    public async ValueTask<PolicyRevision?> ReadPolicyAsync(CancellationToken cancellationToken)
    {
        var history = await ReadPoliciesAsync(includeHistory: false, cancellationToken).ConfigureAwait(false);
        return history.Count == 0 ? null : history[0];
    }

    public ValueTask<IReadOnlyList<PolicyRevision>> ReadPolicyHistoryAsync(CancellationToken cancellationToken)
        => ReadPoliciesAsync(includeHistory: true, cancellationToken);

    private async ValueTask<IReadOnlyList<PolicyRevision>> ReadPoliciesAsync(bool includeHistory, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT p.revision, h.revision, h.canonical, h.digest, h.at_utc, h.actor, h.source
                FROM registry_state r JOIN policy_state p ON p.id=1
                LEFT JOIN policy_revisions h ON h.revision <= p.revision
                WHERE r.id=1 AND r.fence=$fence AND r.site_id=$site AND r.schema_version=1 AND p.schema_version=1
                ORDER BY h.revision DESC LIMIT $limit
                """;
            command.Parameters.AddWithValue("$fence", _fence);
            command.Parameters.AddWithValue("$site", _siteId);
            command.Parameters.AddWithValue("$limit", includeHistory ? 65 : 1);
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using var readerLifetime = reader.ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("Policy writer fence or schema is invalid.");
            var head = reader.GetInt64(0);
            if (head < 0) throw new InvalidDataException("Policy revision is invalid.");
            var history = new List<PolicyRevision>();
            if (head == 0)
            {
                if (!await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("Uncommitted policy history exists.");
                return history.AsReadOnly();
            }
            do
            {
                if (await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false) || history.Count >= 64 || reader.GetInt64(1) != head - history.Count)
                    throw new InvalidDataException("Policy history is missing, discontinuous or exceeds its bound.");
                var bytes = (byte[])reader.GetValue(2);
                var digest = (byte[])reader.GetValue(3);
                if (bytes.Length is < 2 or > 256 * 1024 || digest.Length != 32 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), digest))
                    throw new InvalidDataException("Policy integrity failed.");
                history.Add(new PolicyRevision(reader.GetInt64(1), new UTF8Encoding(false, true).GetString(bytes),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)), reader.GetString(5), reader.GetString(6)));
            } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));
            if (history.Count != Math.Min(head, includeHistory ? 64 : 1)) throw new InvalidDataException("Policy history is incomplete.");
            return history.AsReadOnly();
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<bool> TryCommitPolicyAsync(long expectedPolicyRevision, long expectedRegistryRevision,
        PolicyRevision replacement, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedPolicyRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRegistryRevision);
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.Revision != checked(expectedPolicyRevision + 1)) throw new InvalidDataException("Policy commit must advance one revision.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var transactionLifetime = transaction.ConfigureAwait(false);
            if (!await CheckPolicyAuthorityAsync(transaction, expectedPolicyRevision, expectedRegistryRevision, cancellationToken).ConfigureAwait(false)) return false;
            using var update = _connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE policy_state SET revision=$next WHERE id=1 AND schema_version=1 AND revision=$previous";
            update.Parameters.AddWithValue("$next", replacement.Revision);
            update.Parameters.AddWithValue("$previous", expectedPolicyRevision);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return false;
            using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO policy_revisions(revision, canonical, digest, at_utc, actor, source) VALUES($revision,$canonical,$digest,$at,$actor,$source);
                DELETE FROM policy_revisions WHERE revision <= $revision - 64;
                """;
            var bytes = Encoding.UTF8.GetBytes(replacement.CanonicalJson);
            insert.Parameters.AddWithValue("$revision", replacement.Revision);
            insert.Parameters.AddWithValue("$canonical", bytes);
            insert.Parameters.AddWithValue("$digest", SHA256.HashData(bytes));
            insert.Parameters.AddWithValue("$at", replacement.AcceptedAtUtc.ToUnixTimeMilliseconds());
            insert.Parameters.AddWithValue("$actor", replacement.Actor);
            insert.Parameters.AddWithValue("$source", replacement.Source);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<bool> CheckPolicyAuthorityAsync(SqliteTransaction transaction, long expectedPolicyRevision, long expectedRegistryRevision, CancellationToken cancellationToken)
    {
        using var authority = _connection.CreateCommand();
        authority.Transaction = transaction;
        authority.CommandText = """
            SELECT r.revision, p.revision, h.canonical, h.digest FROM registry_state r JOIN policy_state p ON p.id=1
            LEFT JOIN policy_revisions h ON h.revision=p.revision
            WHERE r.id=1 AND r.fence=$fence AND r.site_id=$site AND r.schema_version=1 AND p.schema_version=1
            """;
        authority.Parameters.AddWithValue("$fence", _fence);
        authority.Parameters.AddWithValue("$site", _siteId);
        var reader = await authority.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using var readerLifetime = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("Policy writer fence or schema is invalid.");
        var policyRevision = reader.GetInt64(1);
        if (policyRevision < 0) throw new InvalidDataException("Policy revision is invalid.");
        if (policyRevision > 0)
        {
            if (await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("Current policy is missing.");
            var bytes = (byte[])reader.GetValue(2);
            var digest = (byte[])reader.GetValue(3);
            if (bytes.Length is < 2 or > 256 * 1024 || digest.Length != 32 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), digest))
                throw new InvalidDataException("Current policy integrity failed.");
        }
        return reader.GetInt64(0) == expectedRegistryRevision && policyRevision == expectedPolicyRevision;
    }
}
