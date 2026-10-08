namespace Mk8.Drava.Application.BLL.Configuration;
public static class ProxyAdminSecurityTokenPolicy
{
    public const string DefaultTokenEnvironmentVariable = "MDRAVA_ADMIN_TOKEN";
    public static bool IsAuthenticationEnabled(ProxyAdminOptions options, Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.RequireAuthentication && !string.IsNullOrEmpty(Resolve(options, readEnvironmentVariable).Token);
    }

    public static ProxyAdminTokenResolution Resolve(ProxyAdminOptions options, Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(options);
        var tokenEnvironmentVariable = NormalizeTokenEnvironmentVariable(options.TokenEnvironmentVariable);
        if (!string.IsNullOrEmpty(options.Token))
        {
            return ProxyAdminTokenResolution.Direct(options.Token, tokenEnvironmentVariable);
        }

        var environmentToken = readEnvironmentVariable(tokenEnvironmentVariable);
        return string.IsNullOrEmpty(environmentToken) ? ProxyAdminTokenResolution.None(tokenEnvironmentVariable) : ProxyAdminTokenResolution.Environment(environmentToken, tokenEnvironmentVariable);
    }

    public static string NormalizeTokenEnvironmentVariable(string? tokenEnvironmentVariable)
    {
        return string.IsNullOrWhiteSpace(tokenEnvironmentVariable) ? DefaultTokenEnvironmentVariable : tokenEnvironmentVariable.Trim();
    }

    public static IReadOnlyList<string> NormalizeUrls(IEnumerable<string> urls)
    {
        return urls.Where(static url => !string.IsNullOrWhiteSpace(url)).Select(static url => url.Trim()).ToArray();
    }
}
