namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public abstract record ProxyConfigurationReadResult<TConfiguration>
    where TConfiguration : class
{
    private ProxyConfigurationReadResult()
    {
    }

    public static ProxyConfigurationReadResult<TConfiguration> MissingConfiguration { get; } = new MissingConfigurationResult();

    public static ProxyConfigurationReadResult<TConfiguration> Available(TConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new AvailableResult(configuration);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyConfigurationReadResult<TConfiguration>
    {
        public AvailableResult(TConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            Configuration = configuration;
        }

        public TConfiguration Configuration { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : ProxyConfigurationReadResult<TConfiguration>;
}
