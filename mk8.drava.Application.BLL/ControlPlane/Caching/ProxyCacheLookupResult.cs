namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public abstract record ProxyCacheLookupResult
{
    private ProxyCacheLookupResult()
    {
    }

    public static ProxyCacheLookupResult Miss { get; } = new MissResult();

    public static ProxyCacheLookupResult Hit(CachedProxyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new HitResult(response);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record HitResult : ProxyCacheLookupResult
    {
        public HitResult(CachedProxyResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            Response = response;
        }

        public CachedProxyResponse Response { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissResult : ProxyCacheLookupResult;
}
