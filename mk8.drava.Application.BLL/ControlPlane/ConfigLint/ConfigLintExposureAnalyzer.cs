using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintExposureAnalyzer
{
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintConfigurationSnapshot snapshot, IProxyAdminUrlPolicy adminUrlPolicy, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(adminUrlPolicy);
        ArgumentNullException.ThrowIfNull(snapshot);
        List<ConfigLintFinding> findings = [];
        AddAdminFindings(snapshot, adminUrlPolicy, sourceName, findings);
        AddMetricsFindings(snapshot, sourceName, findings);
        return findings;
    }

    private static void AddAdminFindings(ProxyConfigLintConfigurationSnapshot snapshot, IProxyAdminUrlPolicy adminUrlPolicy, string? sourceName, List<ConfigLintFinding> findings)
    {
        foreach (var url in snapshot.AdminSecurity.Urls)
        {
            if (!adminUrlPolicy.IsNonLocal(url))
            {
                continue;
            }

            var severity = snapshot.AdminSecurity.RequireAuthentication ? "warning" : "error";
            findings.Add(ConfigLintFindingFactory.Create(severity, snapshot.AdminSecurity.RequireAuthentication ? "admin_nonlocal_bind" : "admin_nonlocal_bind_without_auth", snapshot.AdminSecurity.RequireAuthentication ? "Admin API is configured on a non-local bind address and relies on bearer-token authentication." : "Admin API is configured on a non-local bind address without authentication.", sourceName, "admin.urls", "Keep admin binding localhost-only unless remote administration is intentional and authenticated."));
        }
    }

    private static void AddMetricsFindings(ProxyConfigLintConfigurationSnapshot snapshot, string? sourceName, List<ConfigLintFinding> findings)
    {
        if (snapshot.Metrics.PublicMetricsEnabled)
        {
            findings.Add(ConfigLintFindingFactory.Warning("metrics_public_exposure", "Public metrics exposure is configured.", sourceName, "metrics.publicMetricsEnabled", "Prefer the protected /admin/proxy/metrics endpoint."));
        }
    }
}
