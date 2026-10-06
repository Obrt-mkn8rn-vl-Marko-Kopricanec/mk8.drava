using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.DAL.Configuration.Paths;
public sealed class ProxyDataDirectoryPathSafety : IProxyDataDirectoryPathSafety
{
    private const int MaxRelativePathLength = 240;
    public ProxySafeRelativePathResult GetSafeRelativePath(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (!IsInRoot(fullRoot, fullPath))
        {
            return ProxySafeRelativePathResult.Unsafe;
        }

        var relative = Path.GetRelativePath(fullRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        if (relative.Length == 0 || relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return ProxySafeRelativePathResult.Unsafe;
        }

        var safeRelativePath = relative.Length <= MaxRelativePathLength ? relative : string.Concat(relative.AsSpan(0, MaxRelativePathLength - 12), "...truncated");
        return ProxySafeRelativePathResult.Safe(safeRelativePath);
    }

    private static bool IsInRoot(string root, string path)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) || string.Equals(path, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
