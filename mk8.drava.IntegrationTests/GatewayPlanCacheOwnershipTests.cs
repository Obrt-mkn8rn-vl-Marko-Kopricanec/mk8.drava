using Google.Protobuf;
using System.Diagnostics;
using Mk8.Drava.Gateway.Hosting;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.UnitTests;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayPlanCacheOwnershipTests
{
    [Fact]
    public async Task LiveWriterCannotBeReopenedAndReleaseRetainsTheExactCachedPacketAsync()
    {
        using var directory = new RegistryStateDirectory();
        var role = await OpenRoleAsync(directory.Path).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        // Opaque finite protobuf storage fixture; it grants no certificate or serving trust.
        var plan = new PresentationPlan
        {
            Version = 1, SiteId = "cache-fixture", GatewayId = "gateway", Generation = 1,
            EnrollmentCaDer = ByteString.CopyFrom(new byte[160]),
        };
        using (var first = new GatewayPlanCache(role))
        {
            await first.WriteAsync(plan, CancellationToken.None).ConfigureAwait(true);
            Assert.Throws<IOException>(() => { using var second = new GatewayPlanCache(role); });
            Assert.Equal(plan, await first.ReadAsync(CancellationToken.None).ConfigureAwait(true));
        }
        var restoredRole = await OpenRoleAsync(directory.Path).ConfigureAwait(true);
        await using var restoredRoleLifetime = restoredRole.ConfigureAwait(true);
        using var reopened = new GatewayPlanCache(restoredRole);
        Assert.Equal(plan, await reopened.ReadAsync(CancellationToken.None).ConfigureAwait(true));
        var replacement = plan.Clone();
        replacement.Generation = 2;
        await reopened.WriteAsync(replacement, CancellationToken.None).ConfigureAwait(true);
        var persistedRole = await OpenRoleAsync(directory.Path).ConfigureAwait(true);
        await using var persistedRoleLifetime = persistedRole.ConfigureAwait(true);
        Assert.Equal(new SecretBytes(replacement.ToByteArray()), persistedRole.ReadMaterial());
        Assert.False(File.Exists(Path.Combine(directory.Path, "serving.plan")));
    }

    [Fact]
    public async Task ExistingNonPrivateLeaseIsNeverAdoptedOrTruncatedAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var role = await OpenRoleAsync(directory.Path).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var path = Path.Combine(directory.Path, "gateway.lock");
        byte[] original = [1, 2, 3];
        await File.WriteAllBytesAsync(path, original, CancellationToken.None).ConfigureAwait(true);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Assert.Throws<InvalidDataException>(() => { using var cache = new GatewayPlanCache(role); });
        Assert.Equal(original, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(0, role.ReadMaterial().Length);
        Assert.False(File.Exists(Path.Combine(directory.Path, "serving.plan")));
    }

    [Fact]
    public async Task NonRegularLeaseIsRejectedWithoutAdoptingThePipeAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var role = await OpenRoleAsync(directory.Path).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var path = Path.Combine(directory.Path, "gateway.lock");
        var start = new ProcessStartInfo("mkfifo") { UseShellExecute = false };
        start.ArgumentList.Add("--mode=600");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not create the owned FIFO fixture.");
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, process.ExitCode);
        Assert.Throws<InvalidDataException>(() => { using var cache = new GatewayPlanCache(role); });
        Assert.Equal(0, role.ReadMaterial().Length);
        Assert.False(File.Exists(Path.Combine(directory.Path, "serving.plan")));
    }

    private static async Task<PortableRole<GatewayBootstrap>> OpenRoleAsync(string directory)
    {
        var path = Path.Combine(directory, ".env");
        if (!File.Exists(path))
            await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(new GatewayBootstrap { StateDirectory = directory })).ConfigureAwait(false);
        return await PortableConfiguration.LoadRoleDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(false);
    }
}
