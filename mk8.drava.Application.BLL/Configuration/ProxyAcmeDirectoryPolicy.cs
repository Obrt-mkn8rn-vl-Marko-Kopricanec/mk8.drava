namespace Mk8.Drava.Application.BLL.Configuration;
public static class ProxyAcmeDirectoryPolicy
{
    public static string ResolveDirectoryUrl(ProxyAcmeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.DirectoryUrl?.Trim() ?? string.Empty;
    }
}
