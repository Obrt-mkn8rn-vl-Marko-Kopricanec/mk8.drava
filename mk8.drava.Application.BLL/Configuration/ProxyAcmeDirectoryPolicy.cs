namespace Mk8.Drava.Application.BLL.Configuration;
public static class ProxyAcmeDirectoryPolicy
{
    public static string ResolveDirectoryUrl(ProxyAcmeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.IsNullOrWhiteSpace(options.DirectoryUrl))
        {
            return options.DirectoryUrl.Trim();
        }

        return options.UseStaging ? "https://acme-staging-v02.api.letsencrypt.org/directory" : "https://acme-v02.api.letsencrypt.org/directory";
    }
}
