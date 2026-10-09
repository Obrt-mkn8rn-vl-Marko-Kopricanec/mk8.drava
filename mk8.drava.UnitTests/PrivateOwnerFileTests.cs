using System.Diagnostics;
using Mk8.Drava.Application.DAL.Storage;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PrivateOwnerFileTests
{
    [Fact]
    public void LeaseReleaseAllowsTheNextWriterWithoutTruncatingItsFile()
    {
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "writer.lock");
        using (var first = PrivateOwnerFile.AcquireLease(path))
        {
            first.Write([1, 2, 3]); first.Flush();
            Assert.Throws<IOException>(() => { using var second = PrivateOwnerFile.AcquireLease(path); });
        }
        using var next = PrivateOwnerFile.AcquireLease(path); Assert.Equal(3, next.Length);
        Assert.Equal(1, next.ReadByte());
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadAcceptsExactOwnerOnlyBoundedFiles(bool readOnly)
    {
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "credential");
        File.WriteAllBytes(path, [1, 2, 3]);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | (readOnly ? UnixFileMode.None : UnixFileMode.UserWrite));
        Assert.Equal([1, 2, 3], PrivateOwnerFile.Read(path, 3, 3));
        Assert.Throws<InvalidDataException>(() => PrivateOwnerFile.Read(path, 4, 4));
        Assert.Throws<InvalidDataException>(() => PrivateOwnerFile.Read(path, 1, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicPermissionsAndSymbolicLinksAreNeverAdopted(bool link)
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "private");
        File.WriteAllBytes(path, [1]); File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        if (link) { var target = path; path = Path.Combine(directory.Path, "link"); File.CreateSymbolicLink(path, target); }
        else File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Assert.Throws<InvalidDataException>(() => PrivateOwnerFile.Read(path, 1, 1));
        Assert.Throws<InvalidDataException>(() => { using var lease = PrivateOwnerFile.AcquireLease(path); });
    }

    [Fact]
    public async Task NonRegularReadIsRejectedWithoutWaitingForAWriterAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory(); var path = Path.Combine(directory.Path, "fifo");
        var start = new ProcessStartInfo("mkfifo") { UseShellExecute = false };
        start.ArgumentList.Add("--mode=600"); start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not create the owned FIFO control.");
        await process.WaitForExitAsync().ConfigureAwait(true); Assert.Equal(0, process.ExitCode);
        var read = Task.Run(() => Assert.Throws<InvalidDataException>(() => PrivateOwnerFile.Read(path, 1, 16)));
        await read.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        Assert.Throws<InvalidDataException>(() => { using var lease = PrivateOwnerFile.AcquireLease(path); });
    }
}
