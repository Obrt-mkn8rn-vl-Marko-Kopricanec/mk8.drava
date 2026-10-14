using System.Text.Json;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.DAL.Storage;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.CompatibilityTests;

// Disposable synthetic fixture only, never linked into either production host or a published package.
internal static class DevelopmentPolicyCrash
{
    internal static async Task RunAsync(string directory)
    {
        PrivateOwnerFile.RequirePrivateDirectory(directory);
        var (selectedPhase, nonce) = ReadRequest(directory);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(Path.Combine(directory, ".env"), CancellationToken.None).ConfigureAwait(false);
        await using var roleLifetime = role.ConfigureAwait(false);
        if (!string.Equals(role.Configuration.SiteId, "synthetic-crash-site", StringComparison.Ordinal)
            || !string.Equals(role.Configuration.StateDirectory, directory, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Crash fixture role differs from its explicit synthetic owner.");
        }

        if (selectedPhase >= 3)
        {
            var migrating = await SqliteRegistryRepository.OpenForMigrationTestAsync(directory, role.Configuration.SiteId,
                new PortablePolicyPayloadStore(role), phase => (int)phase + 3 == selectedPhase
                    ? WaitForKillAsync(selectedPhase, nonce) : ValueTask.CompletedTask, CancellationToken.None).ConfigureAwait(false);
            await migrating.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("Synthetic migration reached the end without its selected crash boundary.");
        }
        var repository = await SqliteRegistryRepository.OpenAsync(directory, role.Configuration.SiteId, new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(false);
        await using var repositoryLifetime = repository.ConfigureAwait(false);
        repository.PolicyCommitObserver = phase => (int)phase == selectedPhase ? WaitForKillAsync(selectedPhase, nonce) : ValueTask.CompletedTask;
        await repository.TryCommitPolicyAsync(0, 0, new PolicyRevision(1, "{\"value\":1}", DateTimeOffset.UnixEpoch, "fixture", "control"), CancellationToken.None).ConfigureAwait(false);
        throw new InvalidOperationException("Synthetic writer reached the end without its selected crash boundary.");
    }

    private static async ValueTask WaitForKillAsync(int selectedPhase, string nonce)
    {
        await Console.Out.WriteLineAsync("POLICY-CRASH " + JsonSerializer.Serialize(new { kind = "policy-crash-ready", nonce, phase = selectedPhase, pid = Environment.ProcessId })).ConfigureAwait(false);
        await Console.Out.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        // The parent observes this exact phase and kills this owned process without graceful disposal.
        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None).ConfigureAwait(false);
    }

    private static (int Phase, string Nonce) ReadRequest(string directory)
    {
        // This nonsecret test-control document is never a production bootstrap input.
        var request = BootstrapFile.Parse<Dictionary<string, JsonElement>>(PrivateOwnerFile.Read(Path.Combine(directory, "crash.json"), 2, 256));
        if (request.Count != 3 || !request.TryGetValue("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number
            || !schema.TryGetInt32(out var version) || version != 1 || !request.TryGetValue("phase", out var selected) || selected.ValueKind != JsonValueKind.Number
            || !selected.TryGetInt32(out var phase) || phase is < 0 or > 7 || !request.TryGetValue("nonce", out var nonceValue) || nonceValue.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Invalid synthetic policy crash request.");
        }

        var nonce = nonceValue.GetString() ?? throw new InvalidDataException("Absent synthetic policy crash nonce.");
        if (nonce.Length != 32 || nonce.Any(static value => !char.IsAsciiHexDigit(value))) throw new InvalidDataException("Invalid synthetic policy crash nonce.");
        return (phase, nonce);
    }
}
