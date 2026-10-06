using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintConfigurationAnalyzer
{
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintConfigurationSnapshot snapshot, bool activeRuntime, IReadOnlyList<ProxyConfigLintRuntimeListenerState> runtimeListeners, IProxyAdminUrlPolicy adminUrlPolicy, string? sourceName)
    {
        List<ConfigLintFinding> findings = [];
        findings.AddRange(ConfigLintListenerAnalyzer.Analyze(snapshot, activeRuntime, runtimeListeners, sourceName));
        findings.AddRange(ConfigLintRouteOrderingAnalyzer.Analyze(snapshot, sourceName));
        findings.AddRange(ConfigLintRouteAnalyzer.Analyze(snapshot, sourceName));
        findings.AddRange(ConfigLintSiteAnalyzer.Analyze(snapshot, sourceName));
        findings.AddRange(ConfigLintExposureAnalyzer.Analyze(snapshot, adminUrlPolicy, sourceName));
        return findings;
    }
}
