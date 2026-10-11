using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mk8.Drava.Gateway.Hosting;

// Mechanical reuse of the DAL positive owner-lease profile without a business/data dependency.
internal static partial class GatewayWriterLease
{
    public static FileStream Acquire(string path)
    {
        if (OperatingSystem.IsWindows())
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Gateway ownership requires the qualified Windows or Linux lease profile.");
        // O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC | O_NONBLOCK; new files are mode0600.
        var descriptor = OpenDescriptor(path, 0xA0842, 0x180);
        if (descriptor < 0) throw NativeFailure("Gateway writer lease could not be opened without following a link.");
        SafeFileHandle? handle = new((IntPtr)descriptor, ownsHandle: true);
        try
        {
            const uint mask = 0xB; // STATX_TYPE | STATX_MODE | STATX_UID.
            if (ReadStatus(handle, "", 0x1000, mask, out var status) != 0 || (status.Mask & mask) != mask ||
                (status.Mode & 0xF000) != 0x8000 || (status.Mode & 0xFFF) != 0x180 || status.Owner != GetEffectiveUserId())
            {
                throw new InvalidDataException("Gateway writer lease must be a regular owner-only mode0600 file.");
            }
            if (AcquireNativeLock(handle, 6) != 0) throw NativeFailure("Gateway writer lease is unavailable.");
            var stream = new FileStream(handle, FileAccess.ReadWrite);
            handle = null;
            return stream;
        }
        finally { handle?.Dispose(); }
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

    // Same Linux statx UAPI layout as the existing qualified DAL file profile.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Status
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Owner;
        [FieldOffset(28)] public ushort Mode;
    }
}
