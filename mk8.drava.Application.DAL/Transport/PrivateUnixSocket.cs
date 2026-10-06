using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Mk8.Drava.Application.DAL.Transport;

// Caller holds the exclusive Application state lock in a directory private to its service account.
public static partial class PrivateUnixSocket
{
    public static async ValueTask PrepareAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Private socket path must be absolute.");
        if (!OperatingSystem.IsLinux())
        {
            if (File.Exists(path) || new FileInfo(path).LinkTarget is not null) throw new PlatformNotSupportedException("Stale Unix socket recovery requires the Linux file identity adapter.");
            return;
        }
        var identity = ReadIdentity(path);
        if (identity is null) return;
        if ((identity.Value.Mode & 0xF000) != 0xC000) throw new InvalidDataException("Private socket path is occupied by a different file type.");
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await probe.ConnectAsync(new UnixDomainSocketEndPoint(path), timeout.Token).ConfigureAwait(false);
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused)
        {
            var current = ReadIdentity(path);
            if (current is null || current.Value.Mode != identity.Value.Mode || current.Value.Inode != identity.Value.Inode ||
                current.Value.DeviceMajor != identity.Value.DeviceMajor || current.Value.DeviceMinor != identity.Value.DeviceMinor)
                throw new IOException("Private socket identity changed during recovery.", exception);
            File.Delete(path);
            return;
        }
        throw new IOException("An active listener occupies the private socket; recovery cannot remove it.");
    }

    private static LinuxFileStatus? ReadIdentity(string path)
    {
        const uint typeAndInode = 0x101;
        if (Statx(-100, path, 0x100, typeAndInode, out var status) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 2) return null;
            throw new IOException("Private socket file identity could not be read.");
        }
        if ((status.Mask & typeAndInode) != typeAndInode) throw new IOException("Private socket identity is incomplete.");
        return status;
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Statx(int directory, string path, int flags, uint mask, out LinuxFileStatus status);

    // Fixed Linux UAPI statx layout: include/uapi/linux/stat.h (256 bytes, independent of architecture).
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxFileStatus
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
}
