using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public abstract record ProxyStatusConfigurationReadResult
{
    private ProxyStatusConfigurationReadResult()
    {
    }

    public static ProxyStatusConfigurationReadResult MissingConfiguration { get; } = new MissingConfigurationResult();

    public static ProxyStatusConfigurationReadResult Available(ProxyStatusConfigurationSourceSet configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new AvailableResult(configuration);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyStatusConfigurationReadResult
    {
        public AvailableResult(ProxyStatusConfigurationSourceSet configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            Configuration = configuration;
        }

        public ProxyStatusConfigurationSourceSet Configuration { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : ProxyStatusConfigurationReadResult;
}
