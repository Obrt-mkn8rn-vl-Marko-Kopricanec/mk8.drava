using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mk8.Drava.Application.DAL.Storage;

public static partial class PrivateOwnerFile
{
    public static void RequirePrivateDirectory(string path)
    {
        using var directory = OpenPrivateDirectory(path);
    }

    public static void SynchronizeDirectory(string path)
    {
        using var directory = OpenPrivateDirectory(path);
        if (Synchronize(directory) != 0) throw NativeFailure("Private directory synchronization failed.");
    }

    private static SafeFileHandle OpenPrivateDirectory(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Durable registry recovery requires the qualified Linux filesystem profile.");
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal))
            throw new InvalidDataException("Private directory requires a canonical absolute path.");
        for (var parent = new DirectoryInfo(path); parent is not null; parent = parent.Parent)
            if (parent.LinkTarget is not null) throw new InvalidDataException("Private directories cannot traverse symbolic links.");
        var descriptor = OpenDescriptor(path, 0xB0000, mode: 0); // O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC.
        if (descriptor < 0) throw NativeFailure("Private directory could not be opened without following a link.");
        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try
        {
            const uint mask = 0xB;
            if (ReadStatus(handle, "", 0x1000, mask, out var status) != 0 || (status.Mask & mask) != mask ||
                (status.Mode & 0xF000) != 0x4000 || (status.Mode & 0xFFF) != 0x1C0 || status.Owner != GetEffectiveUserId())
            {
                throw new InvalidDataException("Private directory must be owned by the consuming identity with mode0700.");
            }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Synchronize(SafeFileHandle descriptor);
}
