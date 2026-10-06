using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Hosting;

internal static class PrivateApplicationState
{
    public static FileStream Open(ApplicationBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        var directory = Directory.CreateDirectory(bootstrap.StateDirectory);
        if (directory.LinkTarget is not null) throw new InvalidDataException("Application state cannot be a symbolic link.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (bootstrap.Listen.UnixSocketPath.Length > 0 && !string.Equals(Path.GetDirectoryName(bootstrap.Listen.UnixSocketPath), directory.FullName, StringComparison.Ordinal))
            throw new InvalidDataException("The private Unix socket must reside directly in the Application state directory.");
        return new FileStream(Path.Combine(directory.FullName, "application.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
