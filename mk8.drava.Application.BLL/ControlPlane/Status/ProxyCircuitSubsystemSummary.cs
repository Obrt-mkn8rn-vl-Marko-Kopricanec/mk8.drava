namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyCircuitSubsystemSummary
{
    public ProxyCircuitSubsystemSummary(int Enabled, int Open, int HalfOpen, int Closed)
    {
        ProxyStatusFacts.RequireNonNegative(Enabled, nameof(Enabled));
        ProxyStatusFacts.RequireNonNegative(Open, nameof(Open));
        ProxyStatusFacts.RequireNonNegative(HalfOpen, nameof(HalfOpen));
        ProxyStatusFacts.RequireNonNegative(Closed, nameof(Closed));
        this.Enabled = Enabled;
        this.Open = Open;
        this.HalfOpen = HalfOpen;
        this.Closed = Closed;
    }

    public int Enabled { get; }
    public int Open { get; }
    public int HalfOpen { get; }
    public int Closed { get; }
    public static ProxyCircuitSubsystemSummary Unknown { get; } = new(0, 0, 0, 0);
}
