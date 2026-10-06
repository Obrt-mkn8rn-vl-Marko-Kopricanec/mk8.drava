namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
internal static class ConfigLintFindingFactory
{
    public static ConfigLintFinding Info(string code, string message, string? source, string? path, string? suggestedFix)
    {
        return Create("info", code, message, source, path, suggestedFix);
    }

    public static ConfigLintFinding Warning(string code, string message, string? source, string? path, string? suggestedFix)
    {
        return Create("warning", code, message, source, path, suggestedFix);
    }

    public static ConfigLintFinding Error(string code, string message, string? source, string? path, string? suggestedFix)
    {
        return Create("error", code, message, source, path, suggestedFix);
    }

    public static ConfigLintFinding Create(string severity, string code, string message, string? source, string? path, string? suggestedFix)
    {
        return new ConfigLintFinding(severity, code, message, source, path, suggestedFix);
    }
}
