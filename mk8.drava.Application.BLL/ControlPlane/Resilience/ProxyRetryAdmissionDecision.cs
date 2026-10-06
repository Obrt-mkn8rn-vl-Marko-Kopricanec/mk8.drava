using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public abstract record ProxyRetryAdmissionDecision
{
    private ProxyRetryAdmissionDecision()
    {
    }

    public static ProxyRetryAdmissionDecision Allowed { get; } = new AllowedDecision();
    public static ProxyRetryAdmissionDecision NotAllowed { get; } = new NotAllowedDecision();

    public static ProxyRetryAdmissionDecision Skipped(string reason)
    {
        return new SkippedDecision(reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record SkippedDecision : ProxyRetryAdmissionDecision
    {
        public SkippedDecision(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Retry admission skip reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }

    private sealed record AllowedDecision : ProxyRetryAdmissionDecision;
    private sealed record NotAllowedDecision : ProxyRetryAdmissionDecision;
}
