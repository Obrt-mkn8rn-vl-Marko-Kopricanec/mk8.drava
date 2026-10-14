using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Mk8.Drava.UnitTests;

[SupportedOSPlatform("windows")]
internal sealed class WindowsPrivateTestDirectory : IDisposable
{
    internal WindowsPrivateTestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "drava-windows-private-" + Guid.NewGuid().ToString("N"));
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("Windows fixture identity is missing.");
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Path).Create(security);
    }

    internal string Path { get; }
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
