using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed partial class ProtectedPolicyCrashProcessTests
{
    private const string MigrationMarker = "synthetic-policy-secret-migration-crash-marker";

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task KilledMigrationPreservesAcceptedHistoryAndResumesPlaintextCleanupBeforeUseAsync(int phase)
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("drava_policy_migration_crash_").FullName;
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var nonce = Guid.NewGuid().ToString("N");
            await PortableFixtureFile.WriteAsync(Path.Combine(directory, ".env"), PortableConfiguration.Serialize(new ApplicationBootstrap
            { SiteId = "synthetic-crash-site", StateDirectory = directory }), CancellationToken.None).ConfigureAwait(true);
            await PortableFixtureFile.WriteAsync(Path.Combine(directory, "crash.json"), BootstrapFile.Serialize(new { schemaVersion = 1, phase, nonce }), CancellationToken.None).ConfigureAwait(true);
            var original = await SeedLegacyHistoryAsync(directory).ConfigureAwait(true);
            await KillAtBoundaryAsync(directory, nonce, phase).ConfigureAwait(true);
            await AssertInterruptedMigrationAsync(directory, phase).ConfigureAwait(true);
            await AssertResumedMigrationAsync(directory, original).ConfigureAwait(true);
            await AssertCleanupCompletedAsync(directory).ConfigureAwait(true);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<IReadOnlyList<PolicyRevision>> SeedLegacyHistoryAsync(string directory)
    {
        var repository = await SqliteRegistryRepository.OpenAsync(directory, "synthetic-crash-site", CancellationToken.None).ConfigureAwait(false);
        await using var lifetime = repository.ConfigureAwait(false);
        for (var index = 1; index <= 3; index++)
        {
            var canonical = "{\"credential\":\"" + MigrationMarker + index.ToString(CultureInfo.InvariantCulture) + "\"}";
            Assert.True(await repository.TryCommitPolicyAsync(index - 1, 0,
                new PolicyRevision(index, canonical, DateTimeOffset.FromUnixTimeMilliseconds(index), "fixture", "file"), CancellationToken.None).ConfigureAwait(false));
        }
        return await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task AssertInterruptedMigrationAsync(string directory, int phase)
    {
        var connection = MigrationConnection(directory);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            var command = connection.CreateCommand();
            await using var commandLifetime = command.ConfigureAwait(false);
            command.CommandText = "SELECT schema_version FROM policy_state WHERE id=1";
            Assert.Equal(phase <= 4 ? 1L : 2L, await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false));
            command.CommandText = "SELECT revision FROM policy_state WHERE id=1";
            Assert.Equal(3L, await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false));
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('policy_revisions') WHERE name='canonical'";
            Assert.Equal(phase <= 4 ? 1L : 0L, await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false));
            if (phase >= 5)
            {
                command.CommandText = "SELECT cleanup_complete FROM policy_state WHERE id=1";
                Assert.Equal(0L, await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false));
            }
        }
        if (phase >= 5)
        {
            var destination = Path.Combine(directory, "refused-recovery");
            await Assert.ThrowsAsync<InvalidDataException>(() => RegistrySnapshot.CreateProtectedAsync(directory, destination,
                "synthetic-crash-site", 16 * 1024 * 1024, Path.Combine(directory, ".env"),
                allowCredentialEscrow: true, CancellationToken.None).AsTask()).ConfigureAwait(false);
            Assert.False(Directory.Exists(destination));
        }
    }

    private static async Task AssertResumedMigrationAsync(string directory, IReadOnlyList<PolicyRevision> original)
    {
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(Path.Combine(directory, ".env"), CancellationToken.None).ConfigureAwait(false);
        await using var roleLifetime = role.ConfigureAwait(false);
        var store = new PortablePolicyPayloadStore(role);
        var repository = await SqliteRegistryRepository.OpenAsync(directory, "synthetic-crash-site", store, CancellationToken.None).ConfigureAwait(false);
        await using var repositoryLifetime = repository.ConfigureAwait(false);
        Assert.Equal(original, await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(original[0], await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.False(await repository.TryCommitPolicyAsync(2, 0,
            new PolicyRevision(3, original[0].CanonicalJson, DateTimeOffset.UnixEpoch, "fixture", "control"), CancellationToken.None).ConfigureAwait(false));
        Assert.True(await repository.TryCommitPolicyAsync(3, 0,
            new PolicyRevision(4, "{\"value\":4}", DateTimeOffset.UnixEpoch, "fixture", "control"), CancellationToken.None).ConfigureAwait(false));
        var history = await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(4, history.Count);
        Assert.Equal(original, history.Skip(1));
        Assert.Equal(4, (await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(false)).References.Count);
    }

    private static async Task AssertCleanupCompletedAsync(string directory)
    {
        var connection = MigrationConnection(directory);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            var command = connection.CreateCommand();
            await using var commandLifetime = command.ConfigureAwait(false);
            command.CommandText = "SELECT schema_version*10+cleanup_complete FROM policy_state WHERE id=1";
            Assert.Equal(21L, await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false));
            command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE name='policy_revisions_legacy'";
            Assert.Equal(0L, await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false));
        }
        foreach (var path in new[] { Path.Combine(directory, "registry.sqlite"), Path.Combine(directory, "registry.sqlite-wal") })
        {
            if (!File.Exists(path)) continue;
            Assert.DoesNotContain(MigrationMarker, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false)), StringComparison.Ordinal);
        }
    }

    private static SqliteConnection MigrationConnection(string directory) => new(new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(directory, "registry.sqlite"), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
    }.ToString());
}
