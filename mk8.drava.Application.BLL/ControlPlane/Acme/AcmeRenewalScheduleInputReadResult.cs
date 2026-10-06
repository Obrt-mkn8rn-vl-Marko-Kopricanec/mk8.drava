namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public abstract record AcmeRenewalScheduleInputReadResult
{
    private AcmeRenewalScheduleInputReadResult()
    {
    }

    public static AcmeRenewalScheduleInputReadResult MissingConfiguration { get; } = new MissingConfigurationResult();

    public static AcmeRenewalScheduleInputReadResult Available(AcmeRenewalScheduleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new AvailableResult(input);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : AcmeRenewalScheduleInputReadResult
    {
        public AvailableResult(AcmeRenewalScheduleInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            Input = input;
        }

        public AcmeRenewalScheduleInput Input { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : AcmeRenewalScheduleInputReadResult;
}
