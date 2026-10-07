namespace Mk8.Drava.Application.DAL.Acme;

public static class PrivateCertificateFile
{
    public static byte[] Read(string path) => ReadProtected(path, 128, 65536);

    internal static byte[] ReadProtected(string path, int minimumBytes, int maximumBytes)
    {
        ValidatePath(path);
        if (!File.Exists(path)) throw new InvalidDataException("Private material is absent or invalid.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != UnixFileMode.None)
            throw new InvalidDataException("Private certificate material must be restricted to its owner.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length < minimumBytes || file.Length > maximumBytes) throw new InvalidDataException("Private material is absent or invalid.");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new InvalidDataException("Private material changed while reading.");
        return bytes;
    }

    public static ValueTask WriteNewAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        WriteProtectedAsync(path, bytes, overwrite: false, 128, 65536, cancellationToken);

    internal static async ValueTask WriteProtectedAsync(string path, ReadOnlyMemory<byte> bytes, bool overwrite, int minimumBytes, int maximumBytes, CancellationToken cancellationToken)
    {
        ValidatePath(path);
        if (bytes.Length < minimumBytes || bytes.Length > maximumBytes) throw new InvalidDataException("Invalid private material size.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            var stream = new FileStream(temporary, options);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite);
        }
        finally { File.Delete(temporary); }
    }

    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).LinkTarget is not null || !Directory.Exists(Path.GetDirectoryName(path)))
            throw new InvalidDataException("Certificate material requires a private absolute file path.");
        var parent = Path.GetDirectoryName(path)!;
        for (var directory = new DirectoryInfo(parent); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null) throw new InvalidDataException("Private material cannot traverse a symbolic link.");
        if (!OperatingSystem.IsWindows() &&
            (File.GetUnixFileMode(parent) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != UnixFileMode.None)
            throw new InvalidDataException("Certificate material directory must be private to its owner.");
    }
}
