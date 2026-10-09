using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.Dns;
using Mk8.Drava.Application.DAL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class NativeDnsJournalTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ005", Justification = "xUnit Assert.Single verifies exactly one item; LINQ First would remove the cardinality assertion.")]
    private static T Only<T>(IReadOnlyList<T> items) => Assert.Single(items);
    private static DnsJournalScope Records => new("records", new string('A', 64));
    private static DnsMutationEntry Addition() => new(Guid.NewGuid(), "svc.site.test", 1, 300, Convert.ToBase64String(new byte[] { 192, 0, 2, 7 }),
        false, Guid.Empty, 9, "prepared", 0, 0, "", "/v2/operations/test");

    [Fact]
    public async Task PreparedIntentAndExactReceiptSurviveRestartWithoutAuthorityAdoptionAsync()
    {
        using var directory = new RegistryStateDirectory();
        var entry = Addition();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            await repository.CommitDnsJournalAsync(Records, 0, new DnsJournalState(1, [entry]), CancellationToken.None).ConfigureAwait(true);
            entry = entry with { State = "activated", Revision = 10, Serial = 17, ContentHash = new string('a', 64) };
            await repository.CommitDnsJournalAsync(Records, 1, new DnsJournalState(2, [entry]), CancellationToken.None).ConfigureAwait(true);
        }
        var restarted = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = restarted.ConfigureAwait(true);
        var state = await restarted.ReadDnsJournalAsync(Records, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, state.Revision); Assert.Equal(entry, Only(state.Entries));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await restarted.ReadDnsJournalAsync(new DnsJournalScope("records", new string('B', 64)), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        var acme = await restarted.ReadDnsJournalAsync(new DnsJournalScope("acme", new string('B', 64)), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, acme.Revision); Assert.Empty(acme.Entries);
    }

    [Fact]
    public async Task JournalRejectsStaleRevisionIntentRewriteAndReceiptRegressionAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        var entry = Addition();
        await repository.CommitDnsJournalAsync(Records, 0, new DnsJournalState(1, [entry]), CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await repository.CommitDnsJournalAsync(Records, 0, new DnsJournalState(1, [entry]), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.CommitDnsJournalAsync(Records, 1, new DnsJournalState(2, [entry with { Owner = "other.site.test" }]), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        entry = entry with { State = "accepted", Revision = 10, Serial = 17, ContentHash = new string('b', 64) };
        await repository.CommitDnsJournalAsync(Records, 1, new DnsJournalState(2, [entry]), CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.CommitDnsJournalAsync(Records, 2, new DnsJournalState(3, [entry with { State = "prepared", Revision = 0, Serial = 0, ContentHash = "" }]), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.CommitDnsJournalAsync(Records, 2, new DnsJournalState(3, []), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(entry, Only((await repository.ReadDnsJournalAsync(Records, CancellationToken.None).ConfigureAwait(true)).Entries));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrUnknownDurableJsonIsRejectedAsync(bool validDigest)
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        await repository.CommitDnsJournalAsync(Records, 0, new DnsJournalState(1, [Addition()]), CancellationToken.None).ConfigureAwait(true);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory.Path, "registry.sqlite"), Pooling = false }.ToString());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await connection.OpenAsync().ConfigureAwait(true);
        var bytes = "{\"revision\":1,\"entries\":[],\"actor\":\"foreign\"}"u8.ToArray();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dns_mutation_journal SET state=$state,digest=$digest WHERE purpose='records'";
        command.Parameters.AddWithValue("$state", bytes); command.Parameters.AddWithValue("$digest", validDigest ? SHA256.HashData(bytes) : new byte[32]);
        Assert.Equal(1, await command.ExecuteNonQueryAsync().ConfigureAwait(true));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.ReadDnsJournalAsync(Records, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ForeignValueCleanupAndUnacknowledgedCompletionAreRejectedAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        var scope = new DnsJournalScope("acme", new string('C', 64));
        var entry = Addition() with { Owner = "_acme-challenge.site.test", Type = 16, Value = Convert.ToBase64String(new byte[] { 43 }.Concat(new byte[43].Select(static _ => (byte)'A')).ToArray()) };
        var foreignRemoval = entry with { Remove = true, RelatedOperationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.CommitDnsJournalAsync(scope, 0, new DnsJournalState(1, [foreignRemoval]), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        await repository.CommitDnsJournalAsync(scope, 0, new DnsJournalState(1, [entry]), CancellationToken.None).ConfigureAwait(true);
        var acknowledged = entry with { State = "activated", Revision = 10, Serial = 17, ContentHash = new string('c', 64) };
        await repository.CommitDnsJournalAsync(scope, 1, new DnsJournalState(2, [acknowledged]), CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.CommitDnsJournalAsync(scope, 2, new DnsJournalState(3, [acknowledged with { State = "completed" }]), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        var removal = acknowledged with { OperationId = Guid.NewGuid(), Remove = true, RelatedOperationId = entry.OperationId, ExpectedZoneRevision = 10, State = "prepared", Revision = 0, Serial = 0, ContentHash = "" };
        await repository.CommitDnsJournalAsync(scope, 2, new DnsJournalState(3, [acknowledged, removal]), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, (await repository.ReadDnsJournalAsync(scope, CancellationToken.None).ConfigureAwait(true)).Entries.Count);
    }

    [Fact]
    public void NoncanonicalDns01DigestOrWrongProfileIsRejectedBeforeMutation()
    {
        var entry = Addition() with { Owner = "_acme-challenge.site.test", Type = 16, Value = Convert.ToBase64String(new byte[] { 43 }.Concat(Enumerable.Repeat((byte)'A', 42)).Append((byte)'B').ToArray()) };
        Assert.Throws<InvalidDataException>(() => entry.Validate("acme"));
        Assert.Throws<InvalidDataException>(() => Addition().Validate("acme"));
    }
}
