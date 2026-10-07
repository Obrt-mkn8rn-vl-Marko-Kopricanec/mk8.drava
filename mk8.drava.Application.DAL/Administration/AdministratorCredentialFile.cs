namespace Mk8.Drava.Application.DAL.Administration;

public static class AdministratorCredentialFile
{
    public static string Read(string path) => PrivateBearerCredentialFile.Read(path);
}
