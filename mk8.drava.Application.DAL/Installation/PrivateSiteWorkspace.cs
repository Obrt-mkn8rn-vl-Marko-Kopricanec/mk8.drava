namespace Mk8.Drava.Application.DAL.Installation;

public sealed class PrivateSiteWorkspace : IDisposable
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileModeBits = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly string _destination;
    private bool _published;

    private PrivateSiteWorkspace(string destination, string staging)
    {
        _destination = destination;
        StagingDirectory = staging;
    }

    public string StagingDirectory { get; }

    public static PrivateSiteWorkspace Create(string destination)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Site initialization requires verified owner-only Unix permissions; Windows installation is not yet supported.");
        if (!Path.IsPathFullyQualified(destination) || !string.Equals(Path.GetFullPath(destination), destination, StringComparison.Ordinal))
            throw new InvalidDataException("Site initialization requires a canonical absolute destination.");
        var parent = Path.GetDirectoryName(destination);
        if (parent is null || !Directory.Exists(parent) || Path.GetFileName(destination).Length == 0)
            throw new InvalidDataException("Site initialization requires an existing private parent directory.");
        for (var directory = new DirectoryInfo(parent); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null) throw new InvalidDataException("Site initialization cannot traverse symbolic links.");
        if ((File.GetUnixFileMode(parent) & ~DirectoryMode) != UnixFileMode.None)
            throw new InvalidDataException("Site initialization parent must be private to its owner.");
        RequireAbsent(destination);
        var staging = Path.Combine(parent, ".drava-init-" + Guid.NewGuid().ToString("N"));
        RequireAbsent(staging);
        Directory.CreateDirectory(staging, DirectoryMode);
        return new PrivateSiteWorkspace(destination, staging);
    }

    public string CreateDirectory(string relativePath)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Site initialization requires Unix permissions.");
        var path = Inside(relativePath);
        Directory.CreateDirectory(path, DirectoryMode);
        return path;
    }

    public async ValueTask WriteAsync(string relativePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Site initialization requires Unix permissions.");
        if (bytes.Length is < 1 or > 1048576) throw new InvalidDataException("Invalid setup file size.");
        var path = Inside(relativePath);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough, UnixCreateMode = FileModeBits,
        };
        var stream = new FileStream(path, options);
        await using var lifetime = stream.ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Publish()
    {
        if (_published) throw new InvalidOperationException("Site workspace is already published.");
        RequireAbsent(_destination);
        Directory.Move(StagingDirectory, _destination);
        _published = true;
    }

    public void Dispose()
    {
        if (!_published) Directory.Delete(StagingDirectory, recursive: true);
    }

    private string Inside(string relativePath)
    {
        if (_published || string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Setup writes require an unpublished workspace-relative path.");
        var path = Path.GetFullPath(Path.Combine(StagingDirectory, relativePath));
        if (!path.StartsWith(StagingDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Setup path escapes its private workspace.");
        return path;
    }

    private static void RequireAbsent(string path)
    {
        if (File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null)
            throw new IOException("Site initialization never replaces an existing destination.");
    }
}
