using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;

namespace Mk8.Drava.Application.DAL.DataDirectory;
public sealed class ProxyBackupFileSystem : IProxyBackupFileSystem
{
    private readonly IProxyDataDirectoryPathSafety _pathSafety;
    public ProxyBackupFileSystem(IProxyDataDirectoryPathSafety pathSafety)
    {
        _pathSafety = pathSafety;
    }

    public bool DirectoryExists(string root, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return Directory.Exists(ResolveRelativePath(root, relativePath));
    }

    public ProxyBackupFileSystemScanResult ScanDataDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return ProxyBackupFileSystemScanResult.MissingRoot();
        }

        List<ProxyBackupFileSystemEntry> files = [];
        List<ProxyBackupFileSystemWarning> warnings = [];
        ScanDirectory(root, root, files, warnings);
        return ProxyBackupFileSystemScanResult.Scanned(files, warnings);
    }

    public ProxySafeRelativePathResult GetSafeRelativePath(string root, string path)
    {
        return _pathSafety.GetSafeRelativePath(root, path);
    }

    private void ScanDirectory(string root, string directory, List<ProxyBackupFileSystemEntry> files, List<ProxyBackupFileSystemWarning> warnings)
    {
        DirectoryInfo directoryInfo;
        try
        {
            directoryInfo = new DirectoryInfo(directory);
        }
        catch
        {
            warnings.Add(new ProxyBackupFileSystemWarning("directory_unreadable", SafeRelativeOrNull(root, directory)));
            return;
        }

        if (directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            warnings.Add(new ProxyBackupFileSystemWarning("reparse_point_skipped", SafeRelativeOrNull(root, directory)));
            return;
        }

        FileInfo[] directoryFiles;
        try
        {
            directoryFiles = directoryInfo.GetFiles();
        }
        catch
        {
            warnings.Add(new ProxyBackupFileSystemWarning("directory_unreadable", SafeRelativeOrNull(root, directory)));
            return;
        }

        foreach (var file in directoryFiles)
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                warnings.Add(new ProxyBackupFileSystemWarning("reparse_point_skipped", SafeRelativeOrNull(root, file.FullName)));
                continue;
            }

            var safePath = _pathSafety.GetSafeRelativePath(root, file.FullName);
            if (safePath is not ProxySafeRelativePathResult.SafeResult safeRelativePath)
            {
                warnings.Add(new ProxyBackupFileSystemWarning("unsafe_path_skipped", null));
                continue;
            }

            files.Add(new ProxyBackupFileSystemEntry(safeRelativePath.RelativePath, file.Length, file.LastWriteTimeUtc));
        }

        DirectoryInfo[] children;
        try
        {
            children = directoryInfo.GetDirectories();
        }
        catch
        {
            warnings.Add(new ProxyBackupFileSystemWarning("directory_unreadable", SafeRelativeOrNull(root, directory)));
            return;
        }

        foreach (var child in children)
        {
            ScanDirectory(root, child.FullName, files, warnings);
        }
    }

    private static string ResolveRelativePath(string root, string relativePath)
    {
        if (string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            return root;
        }

        return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private string? SafeRelativeOrNull(string root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return _pathSafety.GetSafeRelativePath(root, path)is ProxySafeRelativePathResult.SafeResult safePath ? safePath.RelativePath : null;
    }
}
