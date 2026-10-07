using System.Text;

namespace Mk8.Drava.Application.DAL.Administration;

public static class PrivateBearerCredentialFile
{
    public static string Read(string path)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).LinkTarget is not null)
            throw new InvalidDataException("Private bearer credential requires an absolute private file.");
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Credential directory is missing.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(directory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != UnixFileMode.None)
            throw new InvalidDataException("Private bearer credential directory must be private to its owner.");
        var parent = new DirectoryInfo(directory);
        while (parent is not null)
        {
            if (parent.LinkTarget is not null) throw new InvalidDataException("Private bearer credential path cannot traverse a symbolic link.");
            parent = parent.Parent;
        }
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != UnixFileMode.None)
            throw new InvalidDataException("Private bearer credential must be private to its owner.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 32 or > 256) throw new InvalidDataException("Private bearer credential length is invalid.");
        Span<byte> bytes = stackalloc byte[256];
        var count = checked((int)file.Length);
        file.ReadExactly(bytes[..count]);
        if (file.ReadByte() != -1) throw new InvalidDataException("Private bearer credential changed while reading.");
        foreach (ref readonly var value in bytes[..count])
            if (value is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z') and not (>= (byte)'0' and <= (byte)'9') and not (byte)'_' and not (byte)'-')
                throw new InvalidDataException("Private bearer credential must use base64url characters without whitespace.");
        return Encoding.ASCII.GetString(bytes[..count]);
    }
}
