namespace Mk8.Drava.Application.BLL.Configuration;
public static class AdminBindPolicyInputMapper
{
    public const string OperationalConfigSource = "proxy-operational-config";
    public static AdminBindPolicyInput FromStartupConfiguration(AdminStartupSecurityOptions startupSecurity, IReadOnlyList<string> mdravaAdminUrls, string mdravaAdminUrlsSource, IReadOnlyList<string> aspNetCoreUrls, string aspNetCoreUrlsSource)
    {
        ArgumentNullException.ThrowIfNull(startupSecurity);
        return new AdminBindPolicyInput([new AdminBindCandidate(startupSecurity.Urls, OperationalConfigSource, ApplyToWebHost: true), new AdminBindCandidate(mdravaAdminUrls, mdravaAdminUrlsSource, ApplyToWebHost: true), new AdminBindCandidate(aspNetCoreUrls, aspNetCoreUrlsSource, ApplyToWebHost: false)], startupSecurity);
    }
}
