using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProtectedPolicyShutdownTests
{
    [Fact]
    public async Task ConcurrentShutdownJoinsTheWriterAndAdmittedWaiterBeforeReleasingLeaseAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(directory.Path, directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new BlockedStore(new PortablePolicyPayloadStore(role));
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", store, CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var first = repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).AsTask();
        Task<bool>? second = null;
        try
        {
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System).ConfigureAwait(true);
            second = repository.TryCommitPolicyAsync(1, 0, Revision(2), CancellationToken.None).AsTask();
            Assert.False(second.IsCompleted);
            var closing = repository.DisposeAsync().AsTask();
            var repeated = repository.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.False(repeated.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => repository.ReadPolicyAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
            await Assert.ThrowsAsync<IOException>(() => SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", store, CancellationToken.None).AsTask()).ConfigureAwait(true);
            store.Release.TrySetResult();
            Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System).ConfigureAwait(true));
            Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System).ConfigureAwait(true));
            await Task.WhenAll(closing, repeated).WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System).ConfigureAwait(true);
        }
        finally
        {
            store.Release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System).ConfigureAwait(true);
            if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System).ConfigureAwait(true);
        }
        var reopened = await SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
        await using var reopenedLifetime = reopened.ConfigureAwait(true);
        Assert.Equal(2, (await reopened.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true))?.Revision);
    }

    private static PolicyRevision Revision(int index) => new(index, index == 1 ? "{\"value\":1}" : "{\"value\":2}", DateTimeOffset.UnixEpoch, "operator", "control");

    private sealed class BlockedStore(IPolicyPayloadStore inner) : IPolicyPayloadStore
    {
        private bool _block = true;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<PolicyPayloadSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken) => inner.ReadSnapshotAsync(cancellationToken);

        public async ValueTask<PolicyPayloadReference> StageAsync(string canonicalJson, IReadOnlyList<PolicyPayloadReference> retained, CancellationToken cancellationToken)
        {
            var reference = await inner.StageAsync(canonicalJson, retained, cancellationToken).ConfigureAwait(false);
            if (_block)
            {
                _block = false;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, CancellationToken.None).ConfigureAwait(false);
            }
            return reference;
        }
    }
}
