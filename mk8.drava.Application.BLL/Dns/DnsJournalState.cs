namespace Mk8.Drava.Application.BLL.Dns;

public sealed record DnsJournalState(long Revision, IReadOnlyList<DnsMutationEntry> Entries)
{
    public static DnsJournalState Empty { get; } = new(0, Array.Empty<DnsMutationEntry>());
}
