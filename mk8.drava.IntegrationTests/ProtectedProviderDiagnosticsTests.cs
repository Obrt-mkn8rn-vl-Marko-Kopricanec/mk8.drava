using System.Text.Json;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class ProtectedProviderDiagnosticsTests
{
    [Theory]
    [InlineData("mk8.drava.Application", "--portable-service", "dotenv")]
    [InlineData("mk8.drava.Application", "--node-agent", "dotenv")]
    [InlineData("mk8.drava.Gateway", "--portable-service", "dotenv")]
    [InlineData("mk8.drava.Application", "--portable-service", "role-json")]
    [InlineData("mk8.drava.Application", "--node-agent", "role-json")]
    [InlineData("mk8.drava.Gateway", "--portable-service", "role-json")]
    public async Task InvalidProtectedInputExitsWithoutPrintingItsPrivateTextAsync(string project, string mode, string fault)
    {
        const string canary = "synthetic-private-provider-input-canary";
        var directory = Directory.CreateTempSubdirectory("drava_provider_diagnostic_").FullName;
        try
        {
            var path = Path.Combine(directory, ".env");
            var document = string.Equals(fault, "dotenv", StringComparison.Ordinal) ? canary + "@=synthetic\n" : InvalidRoleDocument(project, mode, canary);
            await PortableFixtureFile.WriteAsync(path, document, CancellationToken.None).ConfigureAwait(true);
            var process = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject(project), path, mode);
            await using var lifetime = process.ConfigureAwait(true);
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                Assert.NotEqual(0, await process.WaitForExitAsync(deadline.Token).ConfigureAwait(true));
                await process.DisposeAsync().ConfigureAwait(true);
                Assert.Contains(string.Equals(fault, "dotenv", StringComparison.Ordinal) ? nameof(SupprocomSecretsException) : nameof(JsonException), process.CapturedLog, StringComparison.Ordinal);
                Assert.DoesNotContain(canary, process.CapturedLog, StringComparison.Ordinal);
            }
            finally
            {
                await process.DisposeAsync().ConfigureAwait(true);
                var evidence = Directory.CreateDirectory(Path.Combine(TwoProcessProxy.FindRoot(), "artifacts", "provider-diagnostic-tests")).FullName;
                await File.WriteAllTextAsync(Path.Combine(evidence, Path.GetFileName(directory) + ".log"), process.CapturedLog, CancellationToken.None).ConfigureAwait(true);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string InvalidRoleDocument(string project, string mode, string canary)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, int>(StringComparer.Ordinal) { [canary] = 1 });
        if (string.Equals(mode, "--node-agent", StringComparison.Ordinal)) return PortableConfiguration.SerializeDocument<NodeAgentBootstrap>(json);
        return string.Equals(project, "mk8.drava.Gateway", StringComparison.Ordinal)
            ? PortableConfiguration.SerializeDocument<GatewayBootstrap>(json) : PortableConfiguration.SerializeDocument<ApplicationBootstrap>(json);
    }
}
