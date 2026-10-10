using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.DAL.DataDirectory;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class BackupScanBoundaryTests
{
    [Fact]
    public void UnreadableChildReportsItsRelativePathAndRetainsReadableEntries()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var root = new RegistryStateDirectory();
        var blocked = Directory.CreateDirectory(Path.Combine(root.Path, "blocked")).FullName;
        var readable = Path.Combine(root.Path, "ordinary.bin");
        File.WriteAllBytes(readable, [1, 2, 3]);
        var written = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(readable, written);
        File.SetUnixFileMode(blocked, UnixFileMode.None);
        try
        {
            var scan = new ProxyBackupFileSystem(new ProxyDataDirectoryPathSafety()).ScanDataDirectory(root.Path);
            Assert.True(scan.RootExists);
            var file = Only(scan.Files);
            Assert.Equal("ordinary.bin", file.RelativePath);
            Assert.Equal(3, file.SizeBytes);
            Assert.Equal(new DateTimeOffset(written), file.LastWriteTimeUtc);
            var warning = Only(scan.Warnings);
            Assert.Equal("directory_unreadable", warning.Code);
            Assert.Equal("blocked", warning.RelativePath);
        }
        finally
        {
            File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileAndDirectoryLinksDoNotAdoptEntriesOutsideTheRoot(bool directory)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var outside = new RegistryStateDirectory();
        using var root = new RegistryStateDirectory();
        var target = Path.Combine(outside.Path, "outside.bin");
        File.WriteAllBytes(target, [4, 5, 6]);
        var link = Path.Combine(root.Path, "linked");
        if (directory) Directory.CreateSymbolicLink(link, outside.Path);
        else File.CreateSymbolicLink(link, target);

        var scan = new ProxyBackupFileSystem(new ProxyDataDirectoryPathSafety()).ScanDataDirectory(root.Path);
        Assert.Empty(scan.Files);
        var warning = Only(scan.Warnings);
        Assert.Equal("reparse_point_skipped", warning.Code);
        Assert.Equal("linked", warning.RelativePath);
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(target));
    }

    [Fact]
    public void UnexpectedPathPolicyFailurePropagatesWithoutBecomingAnUnreadableWarning()
    {
        using var root = new RegistryStateDirectory();
        File.WriteAllBytes(Path.Combine(root.Path, "ordinary.bin"), [1]);
        var fault = new InvalidOperationException("Synthetic path-policy invariant failure.");
        var scanner = new ProxyBackupFileSystem(new FaultedPathPolicy(fault));
        var thrown = Assert.Throws<InvalidOperationException>(() => scanner.ScanDataDirectory(root.Path));
        Assert.Same(fault, thrown);
    }

    private sealed class FaultedPathPolicy(InvalidOperationException fault) : IProxyDataDirectoryPathSafety
    {
        public ProxySafeRelativePathResult GetSafeRelativePath(string root, string path) => throw fault;
    }

    private static T Only<T>(IEnumerable<T> items)
    {
#pragma warning disable HLQ005 // xUnit verifies exactly one entry; First would weaken the assertion.
        return Assert.Single(items);
#pragma warning restore HLQ005
    }
}
