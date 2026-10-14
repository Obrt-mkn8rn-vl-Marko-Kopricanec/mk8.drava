using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Mk8.Drava.Application.DAL.Storage;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class WindowsPrivateDirectoryTests
{
    [WindowsFact]
    public void NewDirectoryHasAnOwnerOnlyDescriptorBeforeItsFirstPrivateWrite()
    {
        using var root = new WindowsPrivateTestDirectory();
        var path = Path.Combine(root.Path, "private");
        WindowsPrivateFile.CreateDirectory(path);
        WindowsPrivateFile.RequireDirectory(path);
        using var identity = WindowsIdentity.GetCurrent();
        var security = new DirectoryInfo(path).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
        var file = Path.Combine(path, "first");
        using (var stream = WindowsPrivateFile.CreateNew(file)) stream.Write([1, 2, 3]);
        Assert.Equal([1, 2, 3], PrivateOwnerFile.Read(file, 3, 3));
    }

    [WindowsFact]
    public void ExistingDirectoriesAndFilesAreNeverReplacedOrTruncated()
    {
        using var root = new WindowsPrivateTestDirectory();
        var path = Path.Combine(root.Path, "existing");
        WindowsPrivateFile.CreateDirectory(path);
        var file = Path.Combine(path, "retained");
        using (var stream = WindowsPrivateFile.CreateNew(file)) stream.Write([7]);
        var before = new DirectoryInfo(path).GetAccessControl().GetSecurityDescriptorBinaryForm();
        Assert.Throws<IOException>(() => WindowsPrivateFile.CreateDirectory(path));
        Assert.Throws<IOException>(() => WindowsPrivateFile.CreateDirectory(file));
        Assert.Equal([7], PrivateOwnerFile.Read(file, 1, 1));
        Assert.Equal(before, new DirectoryInfo(path).GetAccessControl().GetSecurityDescriptorBinaryForm());
    }

    [WindowsFact]
    public void AParentWithBroadAccessIsRefusedBeforeCreation()
    {
        using var root = new WindowsPrivateTestDirectory();
        var path = Path.Combine(root.Path, "absent");
        AddWorldRead(root.Path);
        Assert.Throws<InvalidDataException>(() => WindowsPrivateFile.CreateDirectory(path));
        Assert.False(Directory.Exists(path));
    }

    [WindowsFact]
    public void BroadExistingDirectoryIsRefusedWithoutRepairingItsAccessList()
    {
        using var root = new WindowsPrivateTestDirectory();
        var path = Path.Combine(root.Path, "existing");
        WindowsPrivateFile.CreateDirectory(path);
        AddWorldRead(path);
        var before = new DirectoryInfo(path).GetAccessControl().GetSecurityDescriptorBinaryForm();
        Assert.Throws<InvalidDataException>(() => WindowsPrivateFile.RequireDirectory(path));
        Assert.Equal(before, new DirectoryInfo(path).GetAccessControl().GetSecurityDescriptorBinaryForm());
        Assert.True(Directory.Exists(path));
    }

    private static void AddWorldRead(string path)
    {
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        info.SetAccessControl(security);
    }
}
