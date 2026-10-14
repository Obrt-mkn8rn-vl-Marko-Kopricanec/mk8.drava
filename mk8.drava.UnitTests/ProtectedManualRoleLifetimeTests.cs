using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProtectedManualRoleLifetimeTests
{
    [Fact]
    public async Task ManualRefreshAdmissionIsBoundedAndBothShutdownCallsJoinEveryAcceptedReadAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var inner = PortablePolicyPayloadStoreTests.Store(directory.Path);
        var snapshot = await inner.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        var store = new ControlledStore(inner);
        var role = new PortableRole<ApplicationBootstrap>(new ApplicationBootstrap { SiteId = "synthetic-site" }, store, path,
            snapshot["Drava:ApplicationBootstrap"], snapshot);
        try
        {
            var reads = new List<Task<PortableManualConfigurationDocument>> { role.RefreshManualConfigurationAsync(CancellationToken.None).AsTask() };
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(true);
            for (var index = 0; index < 8; index++) reads.Add(role.RefreshManualConfigurationAsync(CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => role.RefreshManualConfigurationAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
            var first = role.DisposeAsync().AsTask();
            var second = role.DisposeAsync().AsTask();
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.All(reads, static read => Assert.False(read.IsCompleted));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => role.RefreshManualConfigurationAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
            store.Release.TrySetResult();
            var results = await Task.WhenAll(reads).ConfigureAwait(true);
            await Task.WhenAll(first, second).ConfigureAwait(true);
            Assert.All(results, static document => Assert.True(document.IsEmpty));
            Assert.Equal(9, store.Reads);
        }
        finally { store.Release.TrySetResult(); await role.DisposeAsync().ConfigureAwait(true); }
    }

    [Fact]
    public async Task EscapingManualDocumentCannotExpandOrdinaryQuotaOrCreateExportFilesAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var document = new PortableManualConfigurationDocument(new string('\u0001', 1024 * 1024));
        await Assert.ThrowsAsync<InvalidDataException>(() => PortablePolicyEscrow.ExportRuntimeAsync(directory.Path, "synthetic-site",
            new PortableRuntimeDocuments(PortablePolicyArchive.Empty, document), allowCredentialEscrow: true, CancellationToken.None)).ConfigureAwait(true);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    private sealed class ControlledStore(SupprocomSecretFileStore inner) : ISecretStore, ISecretDocumentUpdater
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads { get; private set; }

        public async Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (++Reads == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            return await inner.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => inner.SetAsync(key, value, cancellationToken);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, cancellationToken);
        public Task UpdateDocumentAsync(Func<IReadOnlyList<SupprocomSecretSetting>, IReadOnlyList<SupprocomSecretSetting>> update,
            CancellationToken cancellationToken = default) => inner.UpdateDocumentAsync(update, cancellationToken);
    }
}
