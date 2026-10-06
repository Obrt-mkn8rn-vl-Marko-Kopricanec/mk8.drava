using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public interface IProxyBackupFileSystem
{
    bool DirectoryExists(string root, string relativePath);
    ProxyBackupFileSystemScanResult ScanDataDirectory(string root);
    ProxySafeRelativePathResult GetSafeRelativePath(string root, string path);
}
