namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public abstract record ProxyConfigurationReadProjectionResult<TConfiguration>
    where TConfiguration : class
{
    private ProxyConfigurationReadProjectionResult()
    {
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "This member constructs or represents a case of the closed result union for its own configuration/projection type parameter, preserving typed absent/success/failure states and existing guards. It is not an unrelated static utility on a generic type.")]
    public static ProxyConfigurationReadProjectionResult<TConfiguration> MissingConfiguration { get; } = new MissingConfigurationResult();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "This member constructs or represents a case of the closed result union for its own configuration/projection type parameter, preserving typed absent/success/failure states and existing guards. It is not an unrelated static utility on a generic type.")]
    public static ProxyConfigurationReadProjectionResult<TConfiguration> Available(TConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new AvailableResult(configuration);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyConfigurationReadProjectionResult<TConfiguration>
    {
        public AvailableResult(TConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            Configuration = configuration;
        }

        public TConfiguration Configuration { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : ProxyConfigurationReadProjectionResult<TConfiguration>;
}
