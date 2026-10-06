using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
public abstract record UpgradeRequestValidationDecision
{
    private UpgradeRequestValidationDecision()
    {
    }

    public static UpgradeRequestValidationDecision Accepted(UpgradeRequestInfo upgrade)
    {
        ArgumentNullException.ThrowIfNull(upgrade);
        return new AcceptedDecision(upgrade);
    }

    public static UpgradeRequestValidationDecision Rejected(string reason)
    {
        return new RejectedDecision(reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedDecision(UpgradeRequestInfo Upgrade) : UpgradeRequestValidationDecision;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedDecision : UpgradeRequestValidationDecision
    {
        public RejectedDecision(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Upgrade rejection reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }
}
