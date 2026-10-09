using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Mk8.Drava.Application.DAL.Acme;

namespace Mk8.Drava.Application.DAL.Storage;

public static partial class PrivateOwnerFile
{
    public static FileStream AcquireLease(string path) => Open(path, lease: true);

    public static byte[] Read(string path, int minimumBytes, int maximumBytes)
    {
        if (minimumBytes < 0 || maximumBytes < minimumBytes) throw new ArgumentOutOfRangeException(nameof(minimumBytes));
        using var stream = Open(path, lease: false);
        if (stream.Length < minimumBytes || stream.Length > maximumBytes) throw new InvalidDataException("Private file exceeds its permitted byte bound.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Private file changed while reading.");
        return bytes;
    }

    private static FileStream Open(string path, bool lease)
    {
        PrivateCertificateFile.ValidatePath(path);
        if (OperatingSystem.IsWindows())
            return new FileStream(path, lease ? FileMode.OpenOrCreate : FileMode.Open, lease ? FileAccess.ReadWrite : FileAccess.Read,
                lease ? FileShare.None : FileShare.Read);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Private owner files require the qualified Windows or Linux profile.");
        return OpenLinux(path, lease);
    }

    private static FileStream OpenLinux(string path, bool lease)
    {
        // Linux O_NOFOLLOW | O_CLOEXEC | O_NONBLOCK; leases additionally use O_RDWR | O_CREAT, mode0600.
        var descriptor = OpenDescriptor(path, 0xA0800 | (lease ? 0x42 : 0), 0x180);
        if (descriptor < 0) throw NativeFailure("Private file could not be opened without following a link.");
        SafeFileHandle? handle = new((IntPtr)descriptor, ownsHandle: true);
        try
        {
            RequireOwnerRegularFile(handle, lease);
            if (lease && AcquireNativeLock(handle, 6) != 0) throw NativeFailure("Private writer lease is unavailable.");
            var stream = new FileStream(handle, lease ? FileAccess.ReadWrite : FileAccess.Read);
            handle = null;
            return stream;
        }
        finally { handle?.Dispose(); }
    }

    private static void RequireOwnerRegularFile(SafeFileHandle handle, bool lease)
    {
        const uint mask = 0xB; // STATX_TYPE | STATX_MODE | STATX_UID.
        if (ReadStatus(handle, "", 0x1000, mask, out var status) != 0 || (status.Mask & mask) != mask ||
            (status.Mode & 0xF000) != 0x8000 || status.Owner != GetEffectiveUserId() ||
            (lease ? (status.Mode & 0xFFF) != 0x180 : (status.Mode & 0xFFF) is not (0x100 or 0x180)))
            throw new InvalidDataException("Private file must be a regular file owned by the consuming identity with mode0400/0600; writers require0600.");
    }

    private static IOException NativeFailure(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int OpenDescriptor(string path, int flags, uint mode);

    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int AcquireNativeLock(SafeFileHandle handle, int operation);

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int ReadStatus(SafeFileHandle directory, string path, int flags, uint mask, out Status status);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial uint GetEffectiveUserId();

    // Mechanical extension of accepted mk8.dns PrivateInputType's statx ABI, using the installed Linux UAPI offsets.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Status
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Owner;
        [FieldOffset(28)] public ushort Mode;
    }
}
