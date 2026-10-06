namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public interface IProxyConfigLintOperations
{
    ConfigLintStatus LastActiveStatus { get; }

    ConfigLintResult LintActive();
    ConfigLintResult LintSubmitted(ConfigLintRequest? request);
}
