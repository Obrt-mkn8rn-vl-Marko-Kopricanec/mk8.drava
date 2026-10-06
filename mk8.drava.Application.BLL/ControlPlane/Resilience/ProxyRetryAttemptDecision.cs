using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public abstract record ProxyRetryAttemptDecision
{
    private ProxyRetryAttemptDecision()
    {
    }

    public static ProxyRetryAttemptDecision Retry { get; } = new RetryDecision();
    public static ProxyRetryAttemptDecision Stop { get; } = new StopDecision();

    public static ProxyRetryAttemptDecision Skipped(string reason)
    {
        return new SkippedDecision(reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record SkippedDecision : ProxyRetryAttemptDecision
    {
        public SkippedDecision(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Retry attempt skip reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }

    private sealed record RetryDecision : ProxyRetryAttemptDecision;
    private sealed record StopDecision : ProxyRetryAttemptDecision;
}
