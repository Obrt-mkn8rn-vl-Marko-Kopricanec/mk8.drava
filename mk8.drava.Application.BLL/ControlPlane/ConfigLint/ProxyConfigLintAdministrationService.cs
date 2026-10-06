namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed class ProxyConfigLintAdministrationService
{
    private readonly IProxyConfigLintOperations _operations;
    public ProxyConfigLintAdministrationService(IProxyConfigLintOperations operations)
    {
        _operations = operations;
    }

    public ConfigLintResult LintActive()
    {
        return _operations.LintActive();
    }

    public ConfigLintResult LintSubmitted(ConfigLintRequest? request)
    {
        return _operations.LintSubmitted(request);
    }
}
