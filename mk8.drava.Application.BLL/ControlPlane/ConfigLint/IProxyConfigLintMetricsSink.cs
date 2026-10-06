namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public interface IProxyConfigLintMetricsSink
{
    void ConfigLintRun(IReadOnlyList<ConfigLintFinding> findings);
}
