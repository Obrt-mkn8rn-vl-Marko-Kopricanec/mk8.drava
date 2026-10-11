using Mk8.Drava.Application.BLL.Dns;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.DAL.Registry;

public sealed partial class SqliteRegistryRepository
{
    // Reading stored scopes here validates an image; it never provisions runtime DNS/issuer authority.
    internal async ValueTask ValidateBackupJournalsAsync(long registryRevision, CancellationToken cancellationToken)
    {
        var scopes = new List<DnsJournalScope>();
        string? issuerScope = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureDnsJournalAsync(cancellationToken).ConfigureAwait(false);
            var command = _connection.CreateCommand();
            await using var commandLifetime = command.ConfigureAwait(false);
            command.CommandText = "SELECT COUNT(*), MIN(revision), MAX(revision) FROM registry_audit";
            var audit = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (audit.ConfigureAwait(false))
            {
                if (!await audit.ReadAsync(cancellationToken).ConfigureAwait(false) || audit.GetInt64(0) != registryRevision ||
                    (registryRevision > 0 && (audit.GetInt64(1) != 1 || audit.GetInt64(2) != registryRevision)))
                {
                    throw new InvalidDataException("Registry audit history is incomplete.");
                }
            }
            command.CommandText = "SELECT purpose, scope FROM dns_mutation_journal ORDER BY purpose";
            var journals = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (journals.ConfigureAwait(false))
            {
                while (await journals.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (scopes.Count == 2) throw new InvalidDataException("Registry snapshot has too many journal profiles.");
                    var scope = new DnsJournalScope(journals.GetString(0), journals.GetString(1));
                    scope.Validate();
                    scopes.Add(scope);
                }
            }
            command.CommandText = "SELECT scope FROM acme_lifecycle WHERE id=1";
            issuerScope = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (issuerScope is not null) RegistryNames.RequireFingerprint(issuerScope);
        }
        finally { _gate.Release(); }
        for (var index = 0; index < scopes.Count; index++)
            await ReadDnsJournalAsync(scopes[index], cancellationToken).ConfigureAwait(false);
        if (issuerScope is not null) await ReadAcmeStatusAsync(issuerScope, cancellationToken).ConfigureAwait(false);
    }
}
