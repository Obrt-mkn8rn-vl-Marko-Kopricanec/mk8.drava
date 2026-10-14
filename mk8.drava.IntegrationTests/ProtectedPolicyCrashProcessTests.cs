using System.Text.Json;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed partial class ProtectedPolicyCrashProcessTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task KilledWriterHasNoAcceptanceBeforeCommitAndExactlyOneAfterCommitAsync(int phase)
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("drava_policy_crash_").FullName;
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var nonce = Guid.NewGuid().ToString("N");
            await PortableFixtureFile.WriteAsync(Path.Combine(directory, ".env"), PortableConfiguration.Serialize(new ApplicationBootstrap
            { SiteId = "synthetic-crash-site", StateDirectory = directory }), CancellationToken.None).ConfigureAwait(true);
            await PortableFixtureFile.WriteAsync(Path.Combine(directory, "crash.json"), BootstrapFile.Serialize(new { schemaVersion = 1, phase, nonce }), CancellationToken.None).ConfigureAwait(true);
            await KillAtBoundaryAsync(directory, nonce, phase).ConfigureAwait(true);
            await VerifyRecoveredAuthorityAsync(directory, phase).ConfigureAwait(true);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task KillAtBoundaryAsync(string directory, string nonce, int phase)
    {
        var process = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.CompatibilityTests"), directory, "--development-policy-crash");
        var observed = false;
        try
        {
            await using (process.ConfigureAwait(true))
            {
                await WaitForBoundaryAsync(process, nonce, phase).ConfigureAwait(true);
                Assert.False(process.HasExited);
                observed = true;
            }
        }
        finally { await SaveCrashEvidenceAsync(process, nonce, phase, observed).ConfigureAwait(true); }
        Assert.NotNull(process.TerminationExitCode);
        Assert.NotEqual(0, process.TerminationExitCode);
        Assert.True(process.TerminatedByFixture);
    }

    private static async Task WaitForBoundaryAsync(DevelopmentProcess process, string nonce, int phase)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            process.ThrowIfExited();
            var captured = process.CapturedLog;
            var complete = captured.LastIndexOf('\n');
            foreach (var line in (complete < 0 ? "" : captured[..complete]).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.StartsWith("POLICY-CRASH ", StringComparison.Ordinal)) continue;
                using var message = JsonDocument.Parse(line[13..]);
                var root = message.RootElement;
                if (string.Equals(root.GetProperty("kind").GetString(), "policy-crash-ready", StringComparison.Ordinal)
                    && string.Equals(root.GetProperty("nonce").GetString(), nonce, StringComparison.Ordinal)
                    && root.GetProperty("phase").GetInt32() == phase && root.GetProperty("pid").GetInt32() == process.ProcessId)
                {
                    return;
                }
            }
            await Task.Delay(25, deadline.Token).ConfigureAwait(false);
        }
    }

    private static async Task VerifyRecoveredAuthorityAsync(string directory, int phase)
    {
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(Path.Combine(directory, ".env"), CancellationToken.None).ConfigureAwait(false);
        await using var roleLifetime = role.ConfigureAwait(false);
        var store = new PortablePolicyPayloadStore(role);
        var repository = await SqliteRegistryRepository.OpenAsync(directory, "synthetic-crash-site", store, CancellationToken.None).ConfigureAwait(false);
        await using var repositoryLifetime = repository.ConfigureAwait(false);
        var before = await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(false);
        if (phase == 2)
        {
            Assert.NotNull(before);
            Assert.Equal(1, before.Revision);
            Assert.Equal("{\"value\":1}", before.CanonicalJson, StringComparer.Ordinal);
            Assert.False(await repository.TryCommitPolicyAsync(0, 0, new PolicyRevision(1, before.CanonicalJson, DateTimeOffset.UnixEpoch, "fixture", "control"), CancellationToken.None).ConfigureAwait(false));
        }
        else
        {
            Assert.Null(before);
            Assert.Empty(await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(false));
        }
        var next = phase == 2 ? 2 : 1;
        Assert.True(await repository.TryCommitPolicyAsync(next - 1, 0, new PolicyRevision(next, "{\"value\":2}", DateTimeOffset.UnixEpoch, "fixture", "control"), CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(next, (await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(false))?.Revision);
        var snapshot = await store.ReadSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
        if (phase == 2)
        {
            Assert.Equal(2, snapshot.References.Count);
        }
        else
        {
            Assert.Equal(PolicyPayloadSnapshot.Describe("{\"value\":2}"), DevelopmentCardinality.RequireSingle(snapshot.References));
            Assert.Throws<InvalidDataException>(() => snapshot.Resolve(PolicyPayloadSnapshot.Describe("{\"value\":1}")));
        }
    }

    private static async Task SaveCrashEvidenceAsync(DevelopmentProcess process, string nonce, int phase, bool observed)
    {
        var output = Directory.CreateDirectory(Path.Combine(TwoProcessProxy.FindRoot(), "artifacts", "policy-runtime-crash", nonce)).FullName;
        await File.WriteAllTextAsync(Path.Combine(output, "child.log"), process.CapturedLog, CancellationToken.None).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(output, "termination.json"), JsonSerializer.Serialize(new
        { phase, nonce, process.TerminationExitCode, parentObservedLiveBoundary = observed, parentTerminatedOwnedProcess = process.TerminatedByFixture, }), CancellationToken.None).ConfigureAwait(false);
    }
}
