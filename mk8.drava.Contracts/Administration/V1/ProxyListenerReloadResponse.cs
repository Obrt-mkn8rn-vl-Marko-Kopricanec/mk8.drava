namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyListenerReloadResponse
{
    public ProxyListenerReloadResponse(bool succeeded, DateTimeOffset attemptedAtUtc, int added, int removed, int changed, int unchanged, IReadOnlyList<ProxyListenerReloadChangeResponse> changes, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        AttemptedAtUtc = attemptedAtUtc;
        Added = added;
        Removed = removed;
        Changed = changed;
        Unchanged = unchanged;
        Changes = ApiResponseList.Copy(changes);
        Errors = ApiResponseList.Copy(errors);
    }

    public bool Succeeded { get; }
    public DateTimeOffset AttemptedAtUtc { get; }
    public int Added { get; }
    public int Removed { get; }
    public int Changed { get; }
    public int Unchanged { get; }
    public IReadOnlyList<ProxyListenerReloadChangeResponse> Changes { get; }
    public IReadOnlyList<string> Errors { get; }
}
