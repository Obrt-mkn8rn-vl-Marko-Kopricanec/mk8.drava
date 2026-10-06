namespace Mk8.Drava.Application.BLL.Configuration;
public interface IProxyRelativeStoragePathPolicy
{
    bool IsSafeRelativePath(string value);
}
