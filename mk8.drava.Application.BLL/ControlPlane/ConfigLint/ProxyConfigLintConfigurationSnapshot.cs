namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ProxyConfigLintConfigurationSnapshot
{
    public ProxyConfigLintConfigurationSnapshot(IEnumerable<string> SourceFiles, ProxyConfigLintAdminSecurity AdminSecurity, ProxyConfigLintMetricsOptions Metrics, bool Http3QuicConnectionSupported, IEnumerable<ProxyConfigLintListener> Listeners, IEnumerable<ProxyConfigLintRoute> Routes)
    {
        ArgumentNullException.ThrowIfNull(SourceFiles);
        ArgumentNullException.ThrowIfNull(AdminSecurity);
        ArgumentNullException.ThrowIfNull(Metrics);
        ArgumentNullException.ThrowIfNull(Listeners);
        ArgumentNullException.ThrowIfNull(Routes);
        this.SourceFiles = ConfigLintList.Copy(SourceFiles);
        this.AdminSecurity = AdminSecurity;
        this.Metrics = Metrics;
        this.Http3QuicConnectionSupported = Http3QuicConnectionSupported;
        this.Listeners = ConfigLintList.Copy(Listeners);
        this.Routes = ConfigLintList.Copy(Routes);
    }

    public IReadOnlyList<string> SourceFiles { get; }
    public ProxyConfigLintAdminSecurity AdminSecurity { get; }
    public ProxyConfigLintMetricsOptions Metrics { get; }
    public bool Http3QuicConnectionSupported { get; }
    public IReadOnlyList<ProxyConfigLintListener> Listeners { get; }
    public IReadOnlyList<ProxyConfigLintRoute> Routes { get; }
}
