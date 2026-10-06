using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public abstract record ConfigLintSubmittedRequestDecision
{
    private ConfigLintSubmittedRequestDecision()
    {
    }

    public static ConfigLintSubmittedRequestDecision Accepted(ConfigLintSubmittedRequestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new AcceptedDecision(input);
    }

    public static ConfigLintSubmittedRequestDecision Rejected(ConfigLintFinding failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new RejectedDecision(failure);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedDecision : ConfigLintSubmittedRequestDecision
    {
        public AcceptedDecision(ConfigLintSubmittedRequestInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            Input = input;
        }

        public ConfigLintSubmittedRequestInput Input { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedDecision : ConfigLintSubmittedRequestDecision
    {
        public RejectedDecision(ConfigLintFinding failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public ConfigLintFinding Failure { get; }
    }
}
