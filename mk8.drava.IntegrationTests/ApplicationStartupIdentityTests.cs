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
            var socket = Path.Combine(state, "i");
            var bootstrap = new ApplicationBootstrap
            {
                IpcToken = new SecretText(new string('A', 64) + (invalidPermissions ? "" : "!")), SiteId = "startup-fixture", NodeId = "fixture", StateDirectory = state,
                HttpPort = DevelopmentPortAllocator.GetPort(), HttpsPort = 0,
                Listen = new IpcEndpoint { UnixSocketPath = socket },
            };
            var path = Path.Combine(directory, "application.json");
            await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(bootstrap), CancellationToken.None).ConfigureAwait(true);
            if (invalidPermissions) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
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
            var reason = invalidPermissions ? "Portable collection requires a consuming-owner directory0700 and regular file0400/0600." : "Invalid scoped identity encoding.";
            Assert.Contains(reason, process.CapturedLog, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
