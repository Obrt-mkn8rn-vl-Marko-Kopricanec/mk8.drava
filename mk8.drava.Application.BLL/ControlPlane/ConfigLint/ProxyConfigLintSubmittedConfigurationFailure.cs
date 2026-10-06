namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ProxyConfigLintSubmittedConfigurationFailure
{
    public ProxyConfigLintSubmittedConfigurationFailure(ProxyConfigLintSubmittedConfigurationFailureKind kind, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Kind = kind;
        Message = message;
    }

    public ProxyConfigLintSubmittedConfigurationFailureKind Kind { get; }
    public string Message { get; }
}
