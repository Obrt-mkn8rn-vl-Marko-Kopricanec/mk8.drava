namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyConfigSubsystemSummary
{
    public ProxyConfigSubsystemSummary(bool Active, int? Generation, DateTimeOffset? LoadedAtUtc, bool? LastListenerReloadSucceeded, string? LastListenerReloadReason)
    {
        ProxyStatusFacts.RequireOptionalNonNegative(Generation, nameof(Generation));
        ProxyStatusFacts.RequireOptionalText(LastListenerReloadReason, nameof(LastListenerReloadReason));
        this.Active = Active;
        this.Generation = Generation;
        this.LoadedAtUtc = LoadedAtUtc;
        this.LastListenerReloadSucceeded = LastListenerReloadSucceeded;
        this.LastListenerReloadReason = LastListenerReloadReason;
    }

    public bool Active { get; }
    public int? Generation { get; }
    public DateTimeOffset? LoadedAtUtc { get; }
    public bool? LastListenerReloadSucceeded { get; }
    public string? LastListenerReloadReason { get; }
    public static ProxyConfigSubsystemSummary Unknown { get; } = new(false, null, null, null, ProxyStatusText.NotAvailable);
}
