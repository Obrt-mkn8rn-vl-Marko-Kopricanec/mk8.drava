using System.Text.Json;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Acme;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AcmeDns01CleanupJournalTests
{
    [Fact]
    public async Task IntentAcknowledgmentAndRemovalSurviveReopeningWithoutMutatingPriorSnapshotsAsync()
    {
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "cleanup.json");
        var intent = Entry(); IReadOnlyList<AcmeDns01CleanupEntry> pending;
        using (var journal = new AcmeDns01CleanupJournal(path))
        {
            await journal.PutAsync(intent, CancellationToken.None).ConfigureAwait(true); pending = journal.Read();
        }
        using (var journal = new AcmeDns01CleanupJournal(path))
        {
            Assert.Collection(journal.Read(), item => Assert.Equal(intent, item));
            var acknowledged = Entry(intent.OperationId, new string('b', 32));
            await journal.PutAsync(acknowledged, CancellationToken.None).ConfigureAwait(true);
            Assert.Collection(pending, item => Assert.Equal("", item.RecordId));
        }
        using (var journal = new AcmeDns01CleanupJournal(path))
        {
            Assert.Collection(journal.Read(), item => Assert.Equal(new string('b', 32), item.RecordId));
            await journal.RemoveAsync(intent.OperationId, CancellationToken.None).ConfigureAwait(true);
        }
        using var empty = new AcmeDns01CleanupJournal(path); Assert.Empty(empty.Read());
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task ChangedIntentAndRecordIdentityAreRejectedWithoutReplacingDurableBytesAsync()
    {
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "cleanup.json");
        using var journal = new AcmeDns01CleanupJournal(path); var original = Entry(recordId: new string('b', 32));
        await journal.PutAsync(original, CancellationToken.None).ConfigureAwait(true);
        var bytes = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await journal.PutAsync(Entry(original.OperationId, new string('c', 32)), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await journal.PutAsync(Entry(original.OperationId, new string('b', 32), new string('C', 43)), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task FullJournalAndCancellationNeverEvictUnresolvedOperationsAsync()
    {
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "cleanup.json");
        using var journal = new AcmeDns01CleanupJournal(path);
        for (var index = 0; index < 8; index++) await journal.PutAsync(Entry(), CancellationToken.None).ConfigureAwait(true);
        var bytes = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await journal.PutAsync(Entry(), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await journal.RemoveAsync(journal.Read()[0].OperationId, cancellation.Token).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Equal(8, journal.Read().Count);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("{\"version\":2,\"entries\":[]}")]
    [InlineData("{\"version\":1,\"version\":1,\"entries\":[]}")]
    [InlineData("{\"version\":1,\"entries\":[],\"extra\":true}")]
    [InlineData("{\"version\":1,\"entries\":null}")]
    [InlineData("{\"entries\":[]}")]
    public async Task InvalidJournalIsNeverRecreatedOrSilentlyDiscardedAsync(string content)
    {
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "cleanup.json");
        await File.WriteAllTextAsync(path, content, CancellationToken.None).ConfigureAwait(true); Restrict(path);
        Assert.Throws<InvalidDataException>(() => { using var journal = new AcmeDns01CleanupJournal(path); });
        Assert.Equal(content, await File.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task DuplicateOperationsFailClosedAndReleaseTheJournalLockAsync()
    {
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "cleanup.json"); var entry = Entry();
        var content = JsonSerializer.Serialize(new { version = 1, entries = new[] { entry, entry } });
        await File.WriteAllTextAsync(path, content, CancellationToken.None).ConfigureAwait(true); Restrict(path);
        Assert.Throws<InvalidDataException>(() => { using var journal = new AcmeDns01CleanupJournal(path); });
        await File.WriteAllTextAsync(path, "{\"version\":1,\"entries\":[]}", CancellationToken.None).ConfigureAwait(true);
        using var repaired = new AcmeDns01CleanupJournal(path); Assert.Empty(repaired.Read());
    }

    [Fact]
    public void AnotherLiveJournalCannotAcquireTheSameFile()
    {
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "cleanup.json");
        using var first = new AcmeDns01CleanupJournal(path);
        Assert.Throws<IOException>(() => { using var second = new AcmeDns01CleanupJournal(path); });
    }

    [Fact]
    public async Task SharedPermissionsAndSymbolicLinksRejectExistingMaterialAsync()
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "cleanup.json");
        await File.WriteAllTextAsync(path, "{\"version\":1,\"entries\":[]}", CancellationToken.None).ConfigureAwait(true);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        Assert.Throws<InvalidDataException>(() => { using var journal = new AcmeDns01CleanupJournal(path); });
        Restrict(path); var link = Path.Combine(directory.Path, "link.json"); File.CreateSymbolicLink(link, path);
        Assert.Throws<InvalidDataException>(() => { using var journal = new AcmeDns01CleanupJournal(link); });
    }

    internal static AcmeDns01CleanupEntry Entry(string? operationId = null, string recordId = "", string? value = null) =>
        new(new string('a', 32), "site.example", "development", operationId ?? Guid.NewGuid().ToString("N"), "_acme-challenge.site.example", value ?? new string('A', 43), recordId);
    private static void Restrict(string path) { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
}
