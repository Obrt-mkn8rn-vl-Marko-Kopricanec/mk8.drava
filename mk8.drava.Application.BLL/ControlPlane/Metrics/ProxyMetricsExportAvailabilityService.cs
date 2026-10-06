namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed class ProxyMetricsExportAvailabilityService
{
    private readonly IProxyMetricsExportAvailabilityReader _reader;
    public ProxyMetricsExportAvailabilityService(IProxyMetricsExportAvailabilityReader reader)
    {
        _reader = reader;
    }

    public ProxyMetricsExportAvailabilityResult GetAvailability()
    {
        var state = _reader.Read();
        return ProxyMetricsExportAvailabilityResult.FromState(state);
    }
}
