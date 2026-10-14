using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProtectedPolicyRepositoryTests
{
    [Fact]
    public async Task LegacyHistoryMigratesWithExactMetadataAndNoActivePlaintextPayloadsAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var state = CreateState(directory.Path);
        const string marker = "synthetic-policy-secret-migration-marker";
        var legacy = await SqliteRegistryRepository.OpenAsync(state, "synthetic-site", CancellationToken.None).ConfigureAwait(true);
        IReadOnlyList<PolicyRevision> original;
        await using (legacy.ConfigureAwait(true))
        {
            for (var index = 1; index <= 70; index++)
            {
                var policy = new PolicyRevision(index, "{\"credential\":\"" + marker + index.ToString(CultureInfo.InvariantCulture) + "\"}",
                    DateTimeOffset.FromUnixTimeMilliseconds(index), "operator", "control");
                Assert.True(await legacy.TryCommitPolicyAsync(index - 1, 0, policy, CancellationToken.None).ConfigureAwait(true));
            }
            original = await legacy.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true);
        }
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new PortablePolicyPayloadStore(role);
        var migrated = await SqliteRegistryRepository.OpenAsync(state, "synthetic-site", store, CancellationToken.None).ConfigureAwait(true);
        await using (migrated.ConfigureAwait(true))
        {
            Assert.Equal(original, await migrated.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(original[0], await migrated.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(64, (await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(true)).References.Count);
        }
        var database = Path.Combine(state, "registry.sqlite");
        foreach (var candidate in new[] { database, database + "-wal" })
        {
            if (!File.Exists(candidate)) continue;
            Assert.DoesNotContain(marker, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(candidate, CancellationToken.None).ConfigureAwait(true)), StringComparison.Ordinal);
        }
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(true);
        var command = connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(true);
        command.CommandText = "SELECT schema_version*10+cleanup_complete FROM policy_state WHERE id=1";
        Assert.Equal(21L, await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(true));
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('policy_revisions') WHERE name='canonical'";
        Assert.Equal(0L, await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ProtectedCommitsRetainSixtyFourRevisionsAndStaleConflictsNeverStageAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var state = CreateState(directory.Path);
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new CountingStore(new PortablePolicyPayloadStore(role));
        var repository = await SqliteRegistryRepository.OpenAsync(state, "synthetic-site", store, CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        for (var index = 1; index <= 70; index++)
            Assert.True(await repository.TryCommitPolicyAsync(index - 1, 0, Revision(index), CancellationToken.None).ConfigureAwait(true));
        var history = await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(64, history.Count);
        Assert.Equal(70, history[0].Revision);
        Assert.Equal(7, history[^1].Revision);
        var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        var staged = store.Stages;
        Assert.False(await repository.TryCommitPolicyAsync(69, 0, Revision(70), CancellationToken.None).ConfigureAwait(true));
        Assert.False(await repository.TryCommitPolicyAsync(70, 1, Revision(71), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(staged, store.Stages);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
        Assert.True(await repository.TryCommitPolicyAsync(70, 0,
            new PolicyRevision(71, history[^1].CanonicalJson, DateTimeOffset.UnixEpoch, "operator", "control"), CancellationToken.None).ConfigureAwait(true));
        var rolledBack = Assert.IsType<PolicyRevision>(await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(71, rolledBack.Revision);
        Assert.Equal(history[^1].Digest, rolledBack.Digest);
    }

    [Fact]
    public async Task FailedStagingCannotAdvanceSqlAuthorityAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var state = CreateState(directory.Path);
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new CountingStore(new PortablePolicyPayloadStore(role)) { FailStage = true };
        var repository = await SqliteRegistryRepository.OpenAsync(state, "synthetic-site", store, CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        await Assert.ThrowsAsync<IOException>(() => repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Null(await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Empty(await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true));
        store.FailStage = false;
        Assert.True(await repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task MissingProtectedPayloadRefusesHeadHistoryAndNewAcceptanceAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var state = CreateState(directory.Path);
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var store = new CountingStore(new PortablePolicyPayloadStore(role));
        var repository = await SqliteRegistryRepository.OpenAsync(state, "synthetic-site", store, CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        Assert.True(await repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).ConfigureAwait(true));
        store.MissingPayloads = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReadPolicyAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReadPolicyHistoryAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.TryCommitPolicyAsync(1, 0, Revision(2), CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(1, store.Stages);
    }

    private static PolicyRevision Revision(int index) => new(index, "{\"revision\":" + index.ToString(CultureInfo.InvariantCulture) + "}",
        DateTimeOffset.FromUnixTimeMilliseconds(index), "operator", "control");

    private static string CreateState(string parent)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Synthetic protected policy state requires Linux.");
        var path = Path.Combine(parent, "state");
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private sealed class CountingStore(IPolicyPayloadStore inner) : IPolicyPayloadStore
    {
        internal int Stages { get; private set; }
        internal bool FailStage { get; set; }
        internal bool MissingPayloads { get; set; }

        public ValueTask<PolicyPayloadSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken) => MissingPayloads
            ? ValueTask.FromResult(new PolicyPayloadSnapshot("synthetic-site", [])) : inner.ReadSnapshotAsync(cancellationToken);

        public ValueTask<PolicyPayloadReference> StageAsync(string canonicalJson, IReadOnlyList<PolicyPayloadReference> retained, CancellationToken cancellationToken)
        {
            Stages++;
            if (FailStage) throw new IOException("Controlled policy stage failure.");
            return inner.StageAsync(canonicalJson, retained, cancellationToken);
        }
    }
}
