using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mk8.Drava.Configuration;

// Linux mechanically reuses the existing qualified private owner-file ABI. Windows retains its
// existing filesystem trust profile with bounded reads; this does not certify Windows ACLs.
internal static partial class InstallationDocumentGuard
{
    public static void Require(string path, long minimumBytes, long maximumBytes)
    {
        var parent = RequireLocation(path);
        using var stream = Open(path, parent);
        RequireLength(stream, minimumBytes, maximumBytes);
    }

    internal static void RequireDurabilitySupport()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Protected policy mutation requires the qualified Linux durability profile.");
    }

    internal static void RequirePrivateDirectory(string path)
    {
        RequireDurabilitySupport();
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal))
            throw new InvalidDataException("Protected policy environment requires a canonical absolute directory.");
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (current.LinkTarget is not null) throw new InvalidDataException("Protected policy environment cannot traverse symbolic directories.");
        using var directory = OpenDirectory(path);
    }

    internal static void Synchronize(string path, long minimumBytes, long maximumBytes)
    {
        RequireDurabilitySupport();
        var parent = RequireLocation(path);
        using var directory = OpenDirectory(parent);
        using var stream = OpenRelativeFile(path, directory);
        RequireLength(stream, minimumBytes, maximumBytes);
        if (SynchronizeDescriptor(stream.SafeFileHandle) != 0) throw NativeFailure("Protected collection file synchronization failed.");
        if (SynchronizeDescriptor(directory) != 0) throw NativeFailure("Protected collection directory synchronization failed.");
    }

    private static string RequireLocation(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal) ||
            new FileInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException("Portable collection requires a canonical absolute non-symbolic file.");
        }
        var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Portable collection requires a private parent.");
        for (var current = new DirectoryInfo(parent); current is not null; current = current.Parent)
            if (current.LinkTarget is not null) throw new InvalidDataException("Portable collection cannot traverse symbolic directories.");
        return parent;
    }

    private static void RequireLength(FileStream stream, long minimumBytes, long maximumBytes)
    {
        if (stream.Length < minimumBytes || stream.Length > maximumBytes)
            throw new InvalidDataException("Portable collection exceeds its file bounds.");
    }

    private static FileStream Open(string path, string parent)
    {
        if (OperatingSystem.IsWindows()) return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Portable collection requires the supported Linux or Windows file profile.");
        using var directory = OpenDirectory(parent);
        return OpenRelativeFile(path, directory);
    }

    private static SafeFileHandle OpenDirectory(string parent)
    {
        var descriptor = OpenDescriptor(parent, 0xB0000); // O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC.
        if (descriptor < 0) throw NativeFailure("Portable collection parent could not be opened.");
        var directory = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try { RequireStatus(directory, directory: true); return directory; }
        catch { directory.Dispose(); throw; }
    }

    private static FileStream OpenRelativeFile(string path, SafeFileHandle directory)
    {
        var descriptor = OpenRelative(directory, Path.GetFileName(path), 0xA0800); // O_NOFOLLOW | O_CLOEXEC | O_NONBLOCK, read-only.
        if (descriptor < 0) throw NativeFailure("Portable collection file could not be opened without following a link.");
        SafeFileHandle? handle = new((IntPtr)descriptor, ownsHandle: true);
        try
        {
            RequireStatus(handle, directory: false);
            var stream = new FileStream(handle, FileAccess.Read);
            handle = null;
            return stream;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    private static void RequireStatus(SafeFileHandle handle, bool directory)
    {
        const uint mask = 0xB; // STATX_TYPE | STATX_MODE | STATX_UID.
        if (ReadStatus(handle, "", 0x1000, mask, out var status) != 0 || (status.Mask & mask) != mask || status.Owner != GetEffectiveUserId() ||
            (directory ? (status.Mode & 0xF000) != 0x4000 || (status.Mode & 0xFFF) != 0x1C0 : (status.Mode & 0xF000) != 0x8000 || (status.Mode & 0xFFF) is not (0x100 or 0x180)))
        {
            throw new InvalidDataException("Portable collection requires a consuming-owner directory0700 and regular file0400/0600.");
        }
    }

    private static IOException NativeFailure(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int SynchronizeDescriptor(SafeFileHandle descriptor);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int OpenDescriptor(string path, int flags);

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int OpenRelative(SafeFileHandle directory, string path, int flags);

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int ReadStatus(SafeFileHandle directory, string path, int flags, uint mask, out Status status);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial uint GetEffectiveUserId();

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Status
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Owner;
        [FieldOffset(28)] public ushort Mode;
    }
}
