using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Mk8.Drava.Transport.Clients;

// Linux mechanically reuses the existing qualified private owner-file ABI. Windows retains its
// existing filesystem trust profile with bounded reads; this does not certify Windows ACLs.
public static partial class PrivateIpcIdentityFile
{
    public static string Read(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal) ||
            new FileInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException("Scoped identity requires a canonical absolute non-symbolic file.");
        }
        var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Scoped identity requires a private parent.");
        for (var current = new DirectoryInfo(parent); current is not null; current = current.Parent)
        {
            if (current.LinkTarget is not null) throw new InvalidDataException("Scoped identity cannot traverse symbolic directories.");
        }
        using var stream = Open(path, parent);
        var length = stream.Length;
        if (length is < 32 or > 256) throw new InvalidDataException("Scoped identity exceeds its byte bounds.");
        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Scoped identity changed while reading.");
        // Decode only the bounded snapshot; preserve ReadAllText BOM handling and trimming.
        using var snapshot = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(snapshot, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var token = reader.ReadToEnd().Trim();
        if (token.Length is < 32 or > 256 || token.Any(static value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
        {
            throw new InvalidDataException("Invalid scoped identity encoding.");
        }
        return token;
    }

    private static FileStream Open(string path, string parent)
    {
        if (OperatingSystem.IsWindows()) return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Scoped identity requires the supported Linux or Windows file profile.");
        var descriptor = OpenDescriptor(parent, 0xB0000); // O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC.
        if (descriptor < 0) throw NativeFailure("Scoped identity parent could not be opened.");
        using var directory = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        RequireStatus(directory, directory: true);
        descriptor = OpenRelative(directory, Path.GetFileName(path), 0xA0800); // O_NOFOLLOW | O_CLOEXEC | O_NONBLOCK, read-only.
        if (descriptor < 0) throw NativeFailure("Scoped identity file could not be opened without following a link.");
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
            throw new InvalidDataException("Scoped identity requires a consuming-owner directory0700 and regular file0400/0600.");
        }
    }

    private static IOException NativeFailure(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));

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
