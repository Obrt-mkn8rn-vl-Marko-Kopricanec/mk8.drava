using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintSubmittedFailureMapper
{
    public static ConfigLintFinding ToFinding(ProxyConfigLintSubmittedConfigurationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Kind switch
        {
            ProxyConfigLintSubmittedConfigurationFailureKind.JsonParseError => ConfigLintFindingFactory.Error("parse_error", $"JSON is invalid: {SafeMessage(failure.Message)}", SiteConfigurationSource.LintInputPath, null, "Fix the JSON syntax and retry linting."),
            ProxyConfigLintSubmittedConfigurationFailureKind.YamlParseError => ConfigLintFindingFactory.Error("parse_error", $"YAML is invalid: {SafeMessage(failure.Message)}", SiteConfigurationSource.LintInputPath, null, "Fix the YAML syntax and retry linting."),
            _ => ConfigLintFindingFactory.Error("empty_config", "Submitted config did not contain a site object.", SiteConfigurationSource.LintInputPath, null, "Submit one site configuration object.")};
    }

    private static string SafeMessage(string message)
    {
        var sanitized = message.Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.Length > 256 ? sanitized[..256] : sanitized;
    }
}
