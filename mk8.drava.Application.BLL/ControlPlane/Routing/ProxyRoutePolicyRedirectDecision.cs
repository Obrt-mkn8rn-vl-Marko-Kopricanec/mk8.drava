namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public abstract record ProxyRoutePolicyRedirectDecision
{
    private ProxyRoutePolicyRedirectDecision()
    {
    }

    public static ProxyRoutePolicyRedirectDecision NoRedirect { get; } = new NoRedirectDecision();

    public static ProxyRoutePolicyRedirectDecision Redirect(int statusCode, string location)
    {
        return new RedirectDecision(statusCode, location);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RedirectDecision : ProxyRoutePolicyRedirectDecision
    {
        public RedirectDecision(int statusCode, string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Policy redirect location is required.", nameof(location));
            }

            StatusCode = statusCode;
            Location = location;
        }

        public int StatusCode { get; }
        public string Location { get; }
    }

    private sealed record NoRedirectDecision : ProxyRoutePolicyRedirectDecision;
}
