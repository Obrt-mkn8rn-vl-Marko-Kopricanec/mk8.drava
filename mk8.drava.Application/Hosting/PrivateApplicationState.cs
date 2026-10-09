using Mk8.Drava.Configuration;
using Mk8.Drava.Application.DAL.Transport;
using Mk8.Drava.Application.DAL.Storage;

namespace Mk8.Drava.Application.Hosting;

internal static class PrivateApplicationState
{
    public static async ValueTask<FileStream> OpenAsync(ApplicationBootstrap bootstrap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        var directory = Directory.CreateDirectory(bootstrap.StateDirectory);
        if (directory.LinkTarget is not null) throw new InvalidDataException("Application state cannot be a symbolic link.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (bootstrap.Listen.UnixSocketPath.Length > 0 && !string.Equals(Path.GetDirectoryName(bootstrap.Listen.UnixSocketPath), directory.FullName, StringComparison.Ordinal))
            throw new InvalidDataException("The private Unix socket must reside directly in the Application state directory.");
        var lockPath = Path.Combine(directory.FullName, "application.lock");
        if (new FileInfo(lockPath).LinkTarget is not null) throw new InvalidDataException("Application lock cannot be a symbolic link.");
        var writer = PrivateOwnerFile.AcquireLease(lockPath);
        try
        {
            if (bootstrap.Listen.UnixSocketPath.Length > 0) await PrivateUnixSocket.PrepareAsync(bootstrap.Listen.UnixSocketPath, cancellationToken).ConfigureAwait(false);
            return writer;
        }
        catch { await writer.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
