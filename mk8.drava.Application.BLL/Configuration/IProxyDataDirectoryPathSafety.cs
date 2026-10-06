namespace Mk8.Drava.Application.BLL.Configuration;
public interface IProxyDataDirectoryPathSafety
{
    ProxySafeRelativePathResult GetSafeRelativePath(string root, string path);
}
