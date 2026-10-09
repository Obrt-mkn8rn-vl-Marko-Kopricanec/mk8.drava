using System.Text;

namespace Mk8.Drava.Application.INF.Dns.Management;

internal static class ManagementSocketPath
{
    internal static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Private local IPC currently requires Linux.");
        if (!System.IO.Path.IsPathFullyQualified(path) || Encoding.UTF8.GetByteCount(path) > 100)
            throw new ArgumentException("Use an absolute Unix socket path of at most 100 UTF-8 bytes.", nameof(path));
    }

}
