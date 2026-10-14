using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Mk8.Drava.Configuration;

[SupportedOSPlatform("windows")]
internal static partial class WindowsPrivateFile
{
    internal static void RequireDirectory(string path)
    {
        _ = RequirePath(path);
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidDataException("The consuming Windows identity has no user identifier.");
        using var directory = OpenDirectory(path);
        RequireSecurity(directory, owner, directory: true);
    }

    internal static void CreateDirectory(string path)
    {
        var parent = RequirePath(path);
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidDataException("The consuming Windows identity has no user identifier.");
        using var directory = OpenDirectory(parent);
        RequireSecurity(directory, owner, directory: true);
        if (!CreatePrivateDirectory(path, owner))
            throw new IOException("Private Windows directory could not be created without replacing an existing object.", new Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            using var created = OpenDirectory(path);
            RequireSecurity(created, owner, directory: true);
        }
        catch (Exception validationError)
        {
            // Creation succeeded under a checked private parent; remove only the new empty directory.
            try { Directory.Delete(path, recursive: false); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                throw new AggregateException("Private Windows directory validation and empty-directory cleanup failed.", validationError, cleanupError);
            }
            throw;
        }
    }

    private static unsafe bool CreatePrivateDirectory(string path, SecurityIdentifier owner)
    {
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var bytes = security.GetSecurityDescriptorBinaryForm();
        fixed (byte* descriptor = bytes)
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = (nint)descriptor, InheritHandle = 0 };
            return CreateDirectoryNative(path, (nint)(&attributes));
        }
    }
}
