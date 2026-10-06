namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed class ProxyMetricsExportAvailabilityReader : IProxyMetricsExportAvailabilityReader
{
    private readonly IProxyMetricsExportConfigurationSource _configurationSource;
    public ProxyMetricsExportAvailabilityReader(IProxyMetricsExportConfigurationSource configurationSource)
    {
        _configurationSource = configurationSource;
    }

    public ProxyMetricsExportAvailabilityState Read()
    {
        var configurationResult = _configurationSource.ReadConfiguration();
        if (configurationResult is not ProxyMetricsExportConfigurationReadResult.AvailableResult available)
        {
            return ProxyMetricsExportAvailabilityState.MissingConfiguration;
        }

        return ProxyMetricsExportAvailabilityState.FromConfiguration(available.Configuration);
    }
}
