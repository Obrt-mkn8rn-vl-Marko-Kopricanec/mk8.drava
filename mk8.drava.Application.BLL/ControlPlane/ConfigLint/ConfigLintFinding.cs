namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ConfigLintFinding
{
    public ConfigLintFinding(string severity, string code, string message, string? source, string? path, string? suggestedFix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Severity = severity;
        Code = code;
        Message = message;
        Source = source;
        Path = path;
        SuggestedFix = suggestedFix;
    }

    public string Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public string? Source { get; }
    public string? Path { get; }
    public string? SuggestedFix { get; }
}
