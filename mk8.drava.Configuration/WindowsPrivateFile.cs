using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Mk8.Drava.Configuration;

[SupportedOSPlatform("windows")]
internal static partial class WindowsPrivateFile
{
    internal static FileStream Open(string path, bool lease) => OpenCore(path, lease, createNew: false);

    internal static FileStream CreateNew(string path) => OpenCore(path, lease: true, createNew: true);

    private static FileStream OpenCore(string path, bool lease, bool createNew)
    {
        var parent = RequirePath(path);
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidDataException("The consuming Windows identity has no user identifier.");
        using var directory = OpenDirectory(parent);
        RequireSecurity(directory, owner, directory: true);
        SafeFileHandle? handle = lease ? CreateWriter(path, owner, createNew) : OpenNative(path, 0x80000000, 1, 0, 3, 0x00200000, 0);
        try
        {
            RequireObject(handle, directory: false);
            RequireSecurity(handle, owner, directory: false);
            var stream = new FileStream(handle, lease ? FileAccess.ReadWrite : FileAccess.Read);
            handle = null;
            return stream;
        }
        finally { handle?.Dispose(); }
    }

    internal static void RequireParent(string path)
    {
        var parent = RequirePath(path);
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidDataException("The consuming Windows identity has no user identifier.");
        using var directory = OpenDirectory(parent);
        RequireSecurity(directory, owner, directory: true);
    }

    private static string RequirePath(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!Path.IsPathFullyQualified(path) || root is not { Length: 3 } || !char.IsAsciiLetter(root[0]) ||
            root[1] != ':' || !string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase) ||
            path.AsSpan(2).Contains(':') || Path.GetFileName(path).Length == 0)
        {
            throw new InvalidDataException("Private Windows files require a canonical local-drive path without device or stream syntax.");
        }

        foreach (var component in path.Split(Path.DirectorySeparatorChar))
        {
            if (component.EndsWith(' ') || component.EndsWith('.') || WindowsFileNamePolicy.IsDeviceName(component))
                throw new InvalidDataException("Private Windows paths cannot use reserved device names or trailing-dot/space aliases.");
        }

        var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Private Windows files require a parent directory.");
        for (var current = new DirectoryInfo(parent); current is not null; current = current.Parent)
            if (current.LinkTarget is not null) throw new InvalidDataException("Private Windows files cannot traverse symbolic directories.");
        return parent;
    }

    private static SafeFileHandle OpenDirectory(string path)
    {
        // READ_CONTROL | FILE_READ_ATTRIBUTES, shared read/write but no deletion; open the reparse point itself.
        var handle = OpenNative(path, 0x20080, 3, 0, 3, 0x02200000, 0);
        try { RequireObject(handle, directory: true); return handle; }
        catch { handle.Dispose(); throw; }
    }

    private static unsafe SafeFileHandle CreateWriter(string path, SecurityIdentifier owner, bool createNew)
    {
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        var bytes = security.GetSecurityDescriptorBinaryForm();
        fixed (byte* descriptor = bytes)
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = (nint)descriptor, InheritHandle = 0 };
            // Both creation modes receive their private descriptor atomically; OPEN_ALWAYS never truncates an existing lease.
            return OpenNative(path, 0xC0000000, 0, (nint)(&attributes), createNew ? 1u : 4u, createNew ? 0x80200000u : 0x00200000u, 0);
        }
    }

    private static void RequireObject(SafeFileHandle handle, bool directory)
    {
        if (handle.IsInvalid) throw NativeFailure("Private Windows object could not be opened.");
        if (ReadFileType(handle) != 1) throw new InvalidDataException("Private Windows objects must be disk files or directories.");
        if (!ReadAttributes(handle, 9, out var information, 8)) throw NativeFailure("Private Windows object metadata is unavailable.");
        if ((information.Attributes & 0x400) != 0 || ((information.Attributes & 0x10) != 0) != directory)
            throw new InvalidDataException("Private Windows objects must have the expected file type and cannot be reparse points.");
    }

    private static void RequireSecurity(SafeFileHandle handle, SecurityIdentifier owner, bool directory)
    {
        var security = ReadDescriptor(handle);
        const ControlFlags required = ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected;
        if (security.Owner != owner || !security.ControlFlags.HasFlag(required) || security.DiscretionaryAcl is not { Count: > 0 } acl)
            throw new InvalidDataException("Private Windows objects require the consuming owner and a protected owner-only access list.");
        var allowedFlags = directory ? AceFlags.ContainerInherit | AceFlags.ObjectInherit : AceFlags.None;
        foreach (GenericAce ace in acl)
        {
            if (ace is not CommonAce { IsCallback: false, AceQualifier: AceQualifier.AccessAllowed } entry ||
                entry.SecurityIdentifier != owner || (entry.AceFlags & ~allowedFlags) != AceFlags.None)
            {
                throw new InvalidDataException("Private Windows access lists cannot grant another identity access or contain unsupported entries.");
            }
        }
    }

    private static RawSecurityDescriptor ReadDescriptor(SafeFileHandle handle)
    {
        var error = ReadSecurity(handle, 1, 5, out _, out _, out _, out _, out var descriptor);
        try
        {
            if (error != 0) throw new IOException("Private Windows security metadata is unavailable.", new Win32Exception(checked((int)error)));
            if (descriptor == 0) throw new InvalidDataException("Private Windows security metadata is missing.");
            var length = DescriptorLength(descriptor);
            if (length is < 20 or > 131072) throw new InvalidDataException("Invalid private Windows security descriptor bounds.");
            var bytes = new byte[checked((int)length)];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            return new RawSecurityDescriptor(bytes, 0);
        }
        finally { Marshal.FreeHGlobal(descriptor); } // GetSecurityInfo uses LocalAlloc; FreeHGlobal delegates to LocalFree on Windows.
    }

    private static IOException NativeFailure(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));
}
