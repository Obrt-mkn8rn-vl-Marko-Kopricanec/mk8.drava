namespace Mk8.Drava.Application.BLL.Configuration;
public static class ProxyAcmeDirectoryPolicy
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1055", Justification = "Resolution deliberately returns trimmed configuration text or an empty disabled-state value; operational validation owns URI parsing, without choosing or inventing a provider authority.")]
    public static string ResolveDirectoryUrl(ProxyAcmeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.DirectoryUrl?.Trim() ?? string.Empty;
    }
}
