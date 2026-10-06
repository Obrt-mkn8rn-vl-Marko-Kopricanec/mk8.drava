namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyMetricsExportAvailabilityState(bool HasActiveConfiguration, bool MetricsExportEnabled)
{
    public static ProxyMetricsExportAvailabilityState MissingConfiguration { get; } = new(HasActiveConfiguration: false, MetricsExportEnabled: false);

    public static ProxyMetricsExportAvailabilityState FromConfiguration(ProxyMetricsExportConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new ProxyMetricsExportAvailabilityState(HasActiveConfiguration: true, configuration.MetricsEnabled);
    }
}
