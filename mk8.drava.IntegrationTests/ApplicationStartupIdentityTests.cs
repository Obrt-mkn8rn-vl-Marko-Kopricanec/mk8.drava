using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class ApplicationStartupIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidPrivateIdentityNeverPublishesTheApplicationListenerAsync(bool invalidPermissions)
    {
        if (!OperatingSystem.IsLinux()) return;
        // Finite owned Unix IPC fixture; no public listener, issuer, certificate or peer service.
        var directory = Directory.CreateTempSubdirectory("drava-startup-").FullName;
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var state = Directory.CreateDirectory(Path.Combine(directory, "app"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute).FullName;
            var token = Path.Combine(directory, "identity.token");
            await File.WriteAllTextAsync(token, new string('A', 64) + (invalidPermissions ? "" : "!"), CancellationToken.None).ConfigureAwait(true);
            File.SetUnixFileMode(token, UnixFileMode.UserRead | UnixFileMode.UserWrite | (invalidPermissions ? UnixFileMode.GroupRead : UnixFileMode.None));
            var socket = Path.Combine(state, "i");
            var bootstrap = new ApplicationBootstrap
            {
                SiteId = "startup-fixture", NodeId = "fixture", StateDirectory = state,
                HttpPort = DevelopmentPortAllocator.GetPort(), HttpsPort = 0,
                Listen = new IpcEndpoint { UnixSocketPath = socket, IdentityTokenPath = token },
            };
            var path = Path.Combine(directory, "application.json");
            await File.WriteAllTextAsync(path, PortableConfiguration.Serialize(bootstrap), CancellationToken.None).ConfigureAwait(true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var process = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Application"), path);
            await using var lifetime = process.ConfigureAwait(true);
            while (!process.HasExited && !File.Exists(socket))
            {
                await Task.Delay(25, timeout.Token).ConfigureAwait(true);
            }
            Assert.False(File.Exists(socket), "Application published its private listener before refusing its invalid credential.");
            var exit = await process.WaitForExitAsync(timeout.Token).ConfigureAwait(true);
            Assert.NotEqual(0, exit);
            await process.DisposeAsync().ConfigureAwait(true);
            var evidence = Directory.CreateDirectory(Path.Combine(TwoProcessProxy.FindRoot(), "artifacts", "startup-identity-tests")).FullName;
            await File.WriteAllTextAsync(Path.Combine(evidence, Path.GetFileName(directory) + ".log"), process.CapturedLog, CancellationToken.None).ConfigureAwait(true);
            var reason = invalidPermissions ? "Scoped identity requires a consuming-owner directory0700 and regular file0400/0600." : "Invalid scoped identity encoding.";
            Assert.Contains(reason, process.CapturedLog, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
