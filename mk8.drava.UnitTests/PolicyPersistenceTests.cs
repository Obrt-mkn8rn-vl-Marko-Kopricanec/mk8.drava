using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PolicyPersistenceTests
{
    [Fact]
    public async Task PolicyAndRegistryRevisionsAreIndependentAndBothMustMatchBeforeAcceptanceAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        Assert.Null(await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.True(await repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).ConfigureAwait(true));
        Assert.False(await repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).ConfigureAwait(true));
        Assert.False(await repository.TryCommitPolicyAsync(1, 1, Revision(2), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(0, (await repository.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Revision);
        var state = new RegistryState(1, RegistryState.Empty.Grants, RegistryState.Empty.Instances, []);
        await repository.CommitAsync(0, state, new RegistryAudit(DateTimeOffset.UtcNow, "test", "administrator", "site"), CancellationToken.None).ConfigureAwait(true);
        Assert.False(await repository.TryCommitPolicyAsync(1, 0, Revision(2), CancellationToken.None).ConfigureAwait(true));
        Assert.True(await repository.TryCommitPolicyAsync(1, 1, Revision(2), CancellationToken.None).ConfigureAwait(true));
        var history = await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Collection(history, item => Assert.Equal(2, item.Revision), item => Assert.Equal(1, item.Revision));
        Assert.Equal(1, (await repository.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Revision);
    }

    [Fact]
    public async Task RestartRetainsPolicyAuthorityAndExactlySixtyFourRollbackRevisionsAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
            for (var revision = 1; revision <= 70; revision++)
                Assert.True(await repository.TryCommitPolicyAsync(revision - 1, 0, Revision(revision), CancellationToken.None).ConfigureAwait(true));
        var restarted = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = restarted.ConfigureAwait(true);
        var current = await restarted.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(current);
        Assert.Equal(70, current.Revision);
        Assert.Equal("control", current.Source);
        Assert.Equal(Revision(70).Digest, current.Digest);
        var history = await restarted.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(64, history.Count);
        Assert.Equal(70, history[0].Revision);
        Assert.Equal(7, history[^1].Revision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LostFenceOrDamagedPolicyCannotBeReadAsAnAcceptedRevisionAsync(int scenario)
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        Assert.True(await repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).ConfigureAwait(true));
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory.Path, "registry.sqlite"), Pooling = false }.ToString());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await connection.OpenAsync().ConfigureAwait(true);
        using var command = connection.CreateCommand();
        switch (scenario)
        {
            case 0: command.CommandText = "UPDATE registry_state SET fence=fence+1 WHERE id=1"; break;
            case 1: command.CommandText = "UPDATE policy_revisions SET canonical=X'7B7D' WHERE revision=1"; break;
            case 2: command.CommandText = "DELETE FROM policy_revisions WHERE revision=1"; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        await command.ExecuteNonQueryAsync().ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.TryCommitPolicyAsync(1, 0, Revision(2), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }

    private static PolicyRevision Revision(long revision) => new(revision, "{\"site\":{\"algorithm\":1}}", DateTimeOffset.FromUnixTimeMilliseconds(1_000_000), "administrator", "control");
}
