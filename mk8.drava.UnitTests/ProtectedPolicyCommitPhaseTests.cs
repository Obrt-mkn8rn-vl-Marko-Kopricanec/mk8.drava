using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProtectedPolicyCommitPhaseTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationBeforeCommitHasNoSqlEffectAndAfterPreparationCannotRetractAcceptanceAsync(int phase)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(directory.Path, directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new PortablePolicyPayloadStore(role);
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", store, CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource();
        repository.PolicyCommitObserver = async actual =>
        {
            if (actual == (PolicyCommitPhase)phase) await cancellation.CancelAsync().ConfigureAwait(false);
        };
        if (phase == 0)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.TryCommitPolicyAsync(0, 0, Revision(1), cancellation.Token).AsTask()).ConfigureAwait(true);
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Null(await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
            Assert.Empty(await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true));
            _ = UpstreamTrustConfigurationTests.Only((await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true)).References);
        }
        else
        {
            Assert.True(await repository.TryCommitPolicyAsync(0, 0, Revision(1), cancellation.Token).ConfigureAwait(true));
            Assert.Equal(Revision(1), await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
        }
        repository.PolicyCommitObserver = null;
        var head = phase == 0 ? 0 : 1;
        Assert.True(await repository.TryCommitPolicyAsync(head, 0, Revision(head + 1), CancellationToken.None).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparedFailureRollsBackAndLostAcknowledgmentReconcilesKnownCommitAsync(bool committed)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(directory.Path, directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var failure = new IOException("Controlled policy commit boundary failure.");
        repository.PolicyCommitObserver = phase => phase == (committed ? PolicyCommitPhase.Committed : PolicyCommitPhase.MetadataPrepared)
            ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
        if (committed)
        {
            Assert.True(await repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(Revision(1), await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
        }
        else
        {
            var observed = await Assert.ThrowsAsync<IOException>(() => repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).AsTask()).ConfigureAwait(true);
            Assert.Same(failure, observed);
            Assert.Null(await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
        }
        repository.PolicyCommitObserver = null;
        Assert.True(await repository.TryCommitPolicyAsync(committed ? 1 : 0, 0, Revision(committed ? 2 : 1), CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task UnresolvedCommitClosesFurtherPolicyOperationsUntilDurableReopenAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(directory.Path, directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new MissingAfterCommitStore(new PortablePolicyPayloadStore(role));
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", store, CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            repository.PolicyCommitObserver = phase =>
            {
                if (phase != PolicyCommitPhase.Committed) return ValueTask.CompletedTask;
                store.Missing = true;
                return ValueTask.FromException(new IOException("Controlled lost acknowledgment with unavailable protected archive."));
            };
            await Assert.ThrowsAsync<IOException>(() => repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).AsTask()).ConfigureAwait(true);
            store.Missing = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReadPolicyAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.TryCommitPolicyAsync(1, 0, Revision(2), CancellationToken.None).AsTask()).ConfigureAwait(true);
        }
        var reopened = await SqliteRegistryRepository.OpenAsync(directory.Path, "synthetic-site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
        await using var reopenedLifetime = reopened.ConfigureAwait(true);
        Assert.Equal(Revision(1), await reopened.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.False(await reopened.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).ConfigureAwait(true));
        Assert.True(await reopened.TryCommitPolicyAsync(1, 0, Revision(2), CancellationToken.None).ConfigureAwait(true));
    }

    private static PolicyRevision Revision(int revision) => new(revision, revision == 1 ? "{\"value\":1}" : "{\"value\":2}", DateTimeOffset.UnixEpoch, "operator", "control");

    private sealed class MissingAfterCommitStore(IPolicyPayloadStore inner) : IPolicyPayloadStore
    {
        internal bool Missing { get; set; }

        public ValueTask<PolicyPayloadSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken) => Missing
            ? ValueTask.FromResult(new PolicyPayloadSnapshot("synthetic-site", [])) : inner.ReadSnapshotAsync(cancellationToken);

        public ValueTask<PolicyPayloadReference> StageAsync(string canonicalJson, IReadOnlyList<PolicyPayloadReference> retained, CancellationToken cancellationToken) =>
            inner.StageAsync(canonicalJson, retained, cancellationToken);
    }
}
