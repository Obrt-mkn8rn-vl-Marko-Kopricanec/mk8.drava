namespace Mk8.Drava.Application.DAL.Acme;

public static class PrivateCertificateFile
{
    public static byte[] Read(string path)
    {
        ValidatePath(path);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 128 or > 65536) throw new InvalidDataException("Private certificate material is absent or invalid.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != UnixFileMode.None)
            throw new InvalidDataException("Private certificate material must be restricted to its owner.");
        return File.ReadAllBytes(path);
    }

    public static async ValueTask WriteNewAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        ValidatePath(path);
        if (bytes.Length is < 128 or > 65536) throw new InvalidDataException("Invalid private certificate material size.");
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
            File.Move(temporary, path, overwrite: false);
        }
        finally { File.Delete(temporary); }
    }

    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).LinkTarget is not null || !Directory.Exists(Path.GetDirectoryName(path)))
            throw new InvalidDataException("Certificate material requires a private absolute file path.");
        var parent = Path.GetDirectoryName(path)!;
        if (new DirectoryInfo(parent).LinkTarget is not null || (!OperatingSystem.IsWindows() &&
            (File.GetUnixFileMode(parent) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != UnixFileMode.None))
            throw new InvalidDataException("Certificate material directory must be private to its owner.");
    }
}
