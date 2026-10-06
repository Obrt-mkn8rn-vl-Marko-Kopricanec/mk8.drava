namespace Mk8.Drava.Application.BLL.Configuration;
public static class AdminBindPolicy
{
    public const string DefaultAdminUrl = "http://localhost:5041";
    public static AdminBindResolution Resolve(AdminBindPolicyInput input, IProxyAdminUrlPolicy adminUrlPolicy)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(adminUrlPolicy);
        foreach (var candidate in input.Candidates)
        {
            if (candidate.Urls.Count == 0)
            {
                continue;
            }

            return Validate(candidate.Urls, candidate.Source, candidate.ApplyToWebHost, input.StartupSecurity, adminUrlPolicy);
        }

        return Validate([DefaultAdminUrl], "default", applyToWebHost: true, input.StartupSecurity, adminUrlPolicy);
    }

    private static AdminBindResolution Validate(IReadOnlyList<string> urls, string source, bool applyToWebHost, AdminStartupSecurityOptions startupSecurity, IProxyAdminUrlPolicy adminUrlPolicy)
    {
        var normalizedUrls = urls.Where(static url => !string.IsNullOrWhiteSpace(url)).Select(static url => url.Trim()).ToArray();
        if (normalizedUrls.Length == 0)
        {
            throw new InvalidOperationException("MDRAVA admin bind configuration did not contain any URLs.");
        }

        foreach (var url in normalizedUrls)
        {
            if (!adminUrlPolicy.IsValid(url))
            {
                throw new InvalidOperationException($"MDRAVA admin bind URL '{url}' is invalid. Use an absolute http or https URL.");
            }
        }

        var isLocalOnly = normalizedUrls.All(adminUrlPolicy.IsLocal);
        if (!isLocalOnly && !startupSecurity.AuthenticationEnabled)
        {
            throw new InvalidOperationException("MDRAVA admin API is configured to bind a non-local URL, but admin authentication is not enabled with a configured token. " + "Set proxy operational config admin.requireAuthentication to true and provide admin.token or the configured token environment variable.");
        }

        return new AdminBindResolution(normalizedUrls, source, applyToWebHost, isLocalOnly, startupSecurity.RequireAuthentication, startupSecurity.HasConfiguredToken);
    }
}
