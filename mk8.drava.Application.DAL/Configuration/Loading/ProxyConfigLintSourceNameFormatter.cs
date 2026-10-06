using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;
public sealed class ProxyConfigLintSourceNameFormatter : IProxyConfigLintSourceNameFormatter
{
    public string? FormatSourceName(string? sourcePath)
    {
        return string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetFileName(sourcePath);
    }
}
