namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyMetricsExportAvailabilityResult
{
    private ProxyMetricsExportAvailabilityResult(bool hasActiveConfiguration, bool metricsExportEnabled)
    {
        HasActiveConfiguration = hasActiveConfiguration;
        MetricsExportEnabled = metricsExportEnabled;
    }

    public bool HasActiveConfiguration { get; }
    public bool MetricsExportEnabled { get; }
    public bool Available => HasActiveConfiguration && MetricsExportEnabled;

    public static ProxyMetricsExportAvailabilityResult FromState(ProxyMetricsExportAvailabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ProxyMetricsExportAvailabilityResult(hasActiveConfiguration: state.HasActiveConfiguration, metricsExportEnabled: state.MetricsExportEnabled);
    }
}
