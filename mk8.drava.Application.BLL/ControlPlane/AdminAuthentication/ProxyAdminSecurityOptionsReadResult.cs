namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
public sealed record ProxyAdminSecurityOptionsReadResult
{
    private ProxyAdminSecurityOptionsReadResult(bool hasActiveConfiguration, bool requireAuthentication, string? token, int recentAuditCapacity)
    {
        HasActiveConfiguration = hasActiveConfiguration;
        RequireAuthentication = requireAuthentication;
        Token = token;
        RecentAuditCapacity = recentAuditCapacity;
    }

    public bool HasActiveConfiguration { get; }
    public bool RequireAuthentication { get; }
    public string? Token { get; }
    public int RecentAuditCapacity { get; }

    public static ProxyAdminSecurityOptionsReadResult FromActiveConfiguration(bool requireAuthentication, string? token, int recentAuditCapacity)
    {
        return new ProxyAdminSecurityOptionsReadResult(hasActiveConfiguration: true, requireAuthentication: requireAuthentication, token: token, recentAuditCapacity: recentAuditCapacity);
    }

    public static ProxyAdminSecurityOptionsReadResult FromDefaults(bool requireAuthentication, string? token, int recentAuditCapacity)
    {
        return new ProxyAdminSecurityOptionsReadResult(hasActiveConfiguration: false, requireAuthentication: requireAuthentication, token: token, recentAuditCapacity: recentAuditCapacity);
    }
}
