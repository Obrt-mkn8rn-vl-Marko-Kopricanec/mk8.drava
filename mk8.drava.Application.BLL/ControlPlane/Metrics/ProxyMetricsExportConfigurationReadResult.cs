using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public abstract record ProxyMetricsExportConfigurationReadResult
{
    private ProxyMetricsExportConfigurationReadResult()
    {
    }

    public static ProxyMetricsExportConfigurationReadResult MissingConfiguration { get; } = new MissingConfigurationResult();

    public static ProxyMetricsExportConfigurationReadResult Available(ProxyMetricsExportConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new AvailableResult(configuration);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyMetricsExportConfigurationReadResult
    {
        public AvailableResult(ProxyMetricsExportConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            Configuration = configuration;
        }

        public ProxyMetricsExportConfiguration Configuration { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : ProxyMetricsExportConfigurationReadResult;
}
