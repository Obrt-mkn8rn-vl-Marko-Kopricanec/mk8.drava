namespace Mk8.Drava.Application.BLL.Dns;

public interface IDnsMutationJournal
{
    ValueTask<DnsJournalState> ReadDnsJournalAsync(DnsJournalScope scope, CancellationToken cancellationToken);
    ValueTask CommitDnsJournalAsync(DnsJournalScope scope, long expectedRevision, DnsJournalState replacement, CancellationToken cancellationToken);
}
