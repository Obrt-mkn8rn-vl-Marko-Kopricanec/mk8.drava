using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Acme;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class CloudflareAcmeDns01RecoveryTests
{
    [Fact]
    public async Task ChangedAcknowledgedRecordRetainsItsJournalAndNeverDeletesForeignOwnershipAsync()
    {
        using var directory = new RegistryStateDirectory(); await DevelopmentJournaledAcmeProvider.PrepareAsync(directory.Path).ConfigureAwait(true); List<ProviderRecord> records = [];
        using (var session = new DevelopmentJournaledAcmeProvider(directory.Path, records))
            await session.Provider.PublishAsync("_acme-challenge.site.example", new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(true);
        records[0] = records[0] with { Comment = "foreign owner" };
        using var reopened = new DevelopmentJournaledAcmeProvider(directory.Path, records);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await reopened.Provider.RecoverAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Collection(reopened.Journal.Read(), entry => Assert.Equal(records[0].Id, entry.RecordId));
        Assert.DoesNotContain(reopened.Handler.Requests, static r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task RecoveryNeverDeletesAnActiveChallengeAsync()
    {
        using var directory = new RegistryStateDirectory(); await DevelopmentJournaledAcmeProvider.PrepareAsync(directory.Path).ConfigureAwait(true); List<ProviderRecord> records = [];
        using var session = new DevelopmentJournaledAcmeProvider(directory.Path, records);
        var record = await session.Provider.PublishAsync("_acme-challenge.site.example", new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(true);
        var requests = session.Handler.Requests.Length;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await session.Provider.RecoverAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Equal(requests, session.Handler.Requests.Length); Assert.Collection(records, entry => Assert.Equal(record.Host, entry.Name));
        await session.Provider.RemoveAsync(record, CancellationToken.None).ConfigureAwait(true); Assert.Empty(session.Journal.Read());
    }

    [Fact]
    public async Task CanceledRecoveryJoinsItsProviderRequestAndRetainsDurableOwnershipAsync()
    {
        using var directory = new RegistryStateDirectory(); await DevelopmentJournaledAcmeProvider.PrepareAsync(directory.Path).ConfigureAwait(true);
        var intent = AcmeDns01CleanupJournalTests.Entry(recordId: new string('b', 32)); var path = Path.Combine(directory.Path, "cleanup.json");
        using (var journal = new AcmeDns01CleanupJournal(path)) await journal.PutAsync(intent, CancellationToken.None).ConfigureAwait(true);
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
        using var session = new DevelopmentJournaledAcmeProvider(directory.Path, []); session.Handler.Block = true;
        using var cancellation = new CancellationTokenSource(); var operation = session.Provider.RecoverAsync(cancellation.Token).AsTask();
        await session.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        await cancellation.CancelAsync().ConfigureAwait(true);
        var canceled = false;
        try { await operation.ConfigureAwait(true); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.True(canceled);
        Assert.True(session.Handler.Exited.Task.IsCompletedSuccessfully);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path).ConfigureAwait(true)); Assert.Collection(session.Journal.Read(), entry => Assert.Equal(intent, entry));
        Assert.DoesNotContain(session.Handler.Requests, static r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task RestartCleansAcknowledgedChallengeAndPreservesForeignRecordsAsync()
    {
        using var directory = new RegistryStateDirectory(); await DevelopmentJournaledAcmeProvider.PrepareAsync(directory.Path).ConfigureAwait(true);
        var foreign = new ProviderRecord("_acme-challenge.site.example", "TXT", "\"foreign\""); List<ProviderRecord> records = [foreign];
        using (var session = new DevelopmentJournaledAcmeProvider(directory.Path, records))
        {
            await session.Provider.PublishAsync(foreign.Name, new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(true);
            Assert.Collection(session.Journal.Read(), item => Assert.Equal(records[1].Id, item.RecordId));
        }
        using var reopened = new DevelopmentJournaledAcmeProvider(directory.Path, records);
        await reopened.Provider.RecoverAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(reopened.Journal.Read()); Assert.Collection(records, item => Assert.Equal(foreign, item));
    }

    [Fact]
    public async Task LostCreateResponseRetainsIntentThenFindsAndCleansOnlyExactOwnedRecordAsync()
    {
        using var directory = new RegistryStateDirectory(); await DevelopmentJournaledAcmeProvider.PrepareAsync(directory.Path).ConfigureAwait(true);
        List<ProviderRecord> records = []; var operation = Guid.NewGuid().ToString("N");
        using (var session = new DevelopmentJournaledAcmeProvider(directory.Path, records))
        {
            session.Handler.BeforeCreate = () => DevelopmentJournaledAcmeProvider.AssertIntentPersisted(directory.Path, operation);
            session.Handler.Failure = "create-response-lost";
            await Assert.ThrowsAsync<HttpRequestException>(async () => await session.Provider.PublishAsync("_acme-challenge.site.example", new string('A', 43), operation, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
            Assert.Collection(session.Journal.Read(), item => Assert.Equal("", item.RecordId)); Assert.Collection(records, item => Assert.Equal(new string('A', 43), item.Content.Trim('"')));
        }
        using var reopened = new DevelopmentJournaledAcmeProvider(directory.Path, records);
        await reopened.Provider.RecoverAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(reopened.Journal.Read()); Assert.Empty(records);
    }

    [Fact]
    public async Task LostRecoveryDeleteResponsePersistsDiscoveredIdBeforeDeletingAndRestartsSafelyAsync()
    {
        using var directory = new RegistryStateDirectory(); await DevelopmentJournaledAcmeProvider.PrepareAsync(directory.Path).ConfigureAwait(true);
        List<ProviderRecord> records = [];
        using (var session = new DevelopmentJournaledAcmeProvider(directory.Path, records))
        {
            session.Handler.Failure = "create-response-lost";
            await Assert.ThrowsAsync<HttpRequestException>(async () => await session.Provider.PublishAsync("_acme-challenge.site.example", new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        }
        var id = records[0].Id;
        using (var session = new DevelopmentJournaledAcmeProvider(directory.Path, records))
        {
            session.Handler.Failure = "delete-response-lost";
            await Assert.ThrowsAsync<HttpRequestException>(async () => await session.Provider.RecoverAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
            Assert.Empty(records); Assert.Collection(session.Journal.Read(), entry => Assert.Equal(id, entry.RecordId));
        }
        using var recovered = new DevelopmentJournaledAcmeProvider(directory.Path, records);
        await recovered.Provider.RecoverAsync(CancellationToken.None).ConfigureAwait(true); Assert.Empty(recovered.Journal.Read());
    }

    [Fact]
    public async Task MissingUnacknowledgedCreateBlocksIssuanceAndNeverForgetsItsIntentAsync()
    {
        using var directory = new RegistryStateDirectory(); await DevelopmentJournaledAcmeProvider.PrepareAsync(directory.Path).ConfigureAwait(true);
        var intent = AcmeDns01CleanupJournalTests.Entry();
        using (var journal = new AcmeDns01CleanupJournal(Path.Combine(directory.Path, "cleanup.json")))
            await journal.PutAsync(intent, CancellationToken.None).ConfigureAwait(true);
        using var session = new DevelopmentJournaledAcmeProvider(directory.Path, []);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await session.Provider.RecoverAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await session.Provider.PublishAsync(intent.Host, intent.Value, Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Collection(session.Journal.Read(), item => Assert.Equal(intent, item)); Assert.DoesNotContain(session.Handler.Requests, static r => r.Method == HttpMethod.Post || r.Method == HttpMethod.Delete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedOrDuplicateOwnershipCannotBeDeletedDuringRecoveryAsync(bool duplicate)
    {
        using var directory = new RegistryStateDirectory(); await DevelopmentJournaledAcmeProvider.PrepareAsync(directory.Path).ConfigureAwait(true); List<ProviderRecord> records = [];
        using (var session = new DevelopmentJournaledAcmeProvider(directory.Path, records))
        {
            session.Handler.Failure = "create-response-lost";
            await Assert.ThrowsAsync<HttpRequestException>(async () => await session.Provider.PublishAsync("_acme-challenge.site.example", new string('A', 43), Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        }
        records.Add(records[0] with { Id = new string('c', 32), Content = duplicate ? records[0].Content : "\"changed\"" });
        using var reopened = new DevelopmentJournaledAcmeProvider(directory.Path, records);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await reopened.Provider.RecoverAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Collection(reopened.Journal.Read(), static entry => Assert.Equal("", entry.RecordId)); Assert.Equal(2, records.Count);
        Assert.DoesNotContain(reopened.Handler.Requests, static r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task ForeignJournalScopeNeverReachesProviderAsync()
    {
        using var directory = new RegistryStateDirectory(); await DevelopmentJournaledAcmeProvider.PrepareAsync(directory.Path).ConfigureAwait(true);
        using (var journal = new AcmeDns01CleanupJournal(Path.Combine(directory.Path, "cleanup.json")))
        {
            var entry = new AcmeDns01CleanupEntry(new string('c', 32), "site.example", "development", Guid.NewGuid().ToString("N"), "_acme-challenge.site.example", new string('A', 43), "");
            await journal.PutAsync(entry, CancellationToken.None).ConfigureAwait(true);
        }
        using var session = new DevelopmentJournaledAcmeProvider(directory.Path, []);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await session.Provider.RecoverAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Empty(session.Handler.Requests); Assert.Collection(session.Journal.Read(), static entry => Assert.Equal(new string('c', 32), entry.ZoneId));
    }
}
