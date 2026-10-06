namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintSourceNameResolver
{
    public static string ActiveSource(ProxyConfigLintConfigurationSnapshot snapshot, IProxyConfigLintSourceNameFormatter sourceNameFormatter)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.SourceFiles.Count == 1 ? SourceName(snapshot.SourceFiles[0], sourceNameFormatter) ?? "active-config" : "active-config";
    }

    public static string? SourceName(string? path, IProxyConfigLintSourceNameFormatter sourceNameFormatter)
    {
        ArgumentNullException.ThrowIfNull(sourceNameFormatter);
        return sourceNameFormatter.FormatSourceName(path);
    }
}
