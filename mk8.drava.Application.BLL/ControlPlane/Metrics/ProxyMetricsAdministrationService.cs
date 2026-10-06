namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed class ProxyMetricsAdministrationService
{
    private readonly IProxyMetricsExportProvider _exportProvider;
    public ProxyMetricsAdministrationService(IProxyMetricsExportProvider exportProvider)
    {
        _exportProvider = exportProvider;
    }

    public ProxyMetricsExportResult Export()
    {
        return _exportProvider.Export();
    }
}
