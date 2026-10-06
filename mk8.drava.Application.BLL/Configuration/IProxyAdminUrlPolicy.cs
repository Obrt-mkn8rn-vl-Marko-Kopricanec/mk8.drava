namespace Mk8.Drava.Application.BLL.Configuration;
public interface IProxyAdminUrlPolicy
{
    bool IsValid(string url);
    bool IsLocal(string url);
    bool IsNonLocal(string url);
}
