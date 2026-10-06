namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public abstract partial record ProxyListenerReloadResult
{
    private ProxyListenerReloadResult(DateTimeOffset attemptedAtUtc, int added, int removed, int changed, int unchanged, IReadOnlyList<ProxyListenerReloadChange> changes, IReadOnlyList<string> errors)
    {
        AttemptedAtUtc = attemptedAtUtc;
        Added = added;
        Removed = removed;
        Changed = changed;
        Unchanged = unchanged;
        Changes = ProxyListenerList.Copy(changes);
        Errors = ProxyListenerList.Copy(errors);
    }

    public DateTimeOffset AttemptedAtUtc { get; }
    public int Added { get; }
    public int Removed { get; }
    public int Changed { get; }
    public int Unchanged { get; }
    public IReadOnlyList<ProxyListenerReloadChange> Changes { get; }
    public IReadOnlyList<string> Errors { get; }

    public static ProxyListenerReloadResult Applied(DateTimeOffset attemptedAtUtc, int added, int removed, int changed, int unchanged, IReadOnlyList<ProxyListenerReloadChange> changes, IReadOnlyList<string> errors)
    {
        ValidateCountsAndCollections(added, removed, changed, unchanged, changes, errors);
        return new AppliedResult(attemptedAtUtc, added, removed, changed, unchanged, changes, errors);
    }

    public static ProxyListenerReloadResult Failed(DateTimeOffset attemptedAtUtc, int added, int removed, int changed, int unchanged, IReadOnlyList<ProxyListenerReloadChange> changes, IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("A failed listener reload requires at least one error.", nameof(errors));
        }

        ValidateCountsAndCollections(added, removed, changed, unchanged, changes, errors);
        return new FailedResult(attemptedAtUtc, added, removed, changed, unchanged, changes, errors);
    }

    private static void ValidateCountsAndCollections(int added, int removed, int changed, int unchanged, IReadOnlyList<ProxyListenerReloadChange> changes, IReadOnlyList<string> errors)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(added);
        ArgumentOutOfRangeException.ThrowIfNegative(removed);
        ArgumentOutOfRangeException.ThrowIfNegative(changed);
        ArgumentOutOfRangeException.ThrowIfNegative(unchanged);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(errors);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AppliedResult : ProxyListenerReloadResult
    {
        internal AppliedResult(DateTimeOffset attemptedAtUtc, int added, int removed, int changed, int unchanged, IReadOnlyList<ProxyListenerReloadChange> changes, IReadOnlyList<string> errors) : base(attemptedAtUtc, added, removed, changed, unchanged, changes, errors)
        {
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record FailedResult : ProxyListenerReloadResult
    {
        internal FailedResult(DateTimeOffset attemptedAtUtc, int added, int removed, int changed, int unchanged, IReadOnlyList<ProxyListenerReloadChange> changes, IReadOnlyList<string> errors) : base(attemptedAtUtc, added, removed, changed, unchanged, changes, errors)
        {
        }
    }
}
