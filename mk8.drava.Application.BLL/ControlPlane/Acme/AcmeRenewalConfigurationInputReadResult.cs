using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public abstract record AcmeRenewalConfigurationInputReadResult
{
    private AcmeRenewalConfigurationInputReadResult()
    {
    }

    public static AcmeRenewalConfigurationInputReadResult MissingConfiguration { get; } = new MissingConfigurationResult();

    public static AcmeRenewalConfigurationInputReadResult Available(AcmeRenewalConfigurationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new AvailableResult(input);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : AcmeRenewalConfigurationInputReadResult
    {
        public AvailableResult(AcmeRenewalConfigurationInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            Input = input;
        }

        public AcmeRenewalConfigurationInput Input { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : AcmeRenewalConfigurationInputReadResult;
}
