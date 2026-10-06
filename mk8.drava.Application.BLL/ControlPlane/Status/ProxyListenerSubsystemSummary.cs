namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyListenerSubsystemSummary
{
    public ProxyListenerSubsystemSummary(int Configured, int Enabled, int Active, int Failed, int Draining, int Http1Enabled, int Http2Enabled, int Http3Enabled, int QuicReady)
    {
        ProxyStatusFacts.RequireNonNegative(Configured, nameof(Configured));
        ProxyStatusFacts.RequireNonNegative(Enabled, nameof(Enabled));
        ProxyStatusFacts.RequireNonNegative(Active, nameof(Active));
        ProxyStatusFacts.RequireNonNegative(Failed, nameof(Failed));
        ProxyStatusFacts.RequireNonNegative(Draining, nameof(Draining));
        ProxyStatusFacts.RequireNonNegative(Http1Enabled, nameof(Http1Enabled));
        ProxyStatusFacts.RequireNonNegative(Http2Enabled, nameof(Http2Enabled));
        ProxyStatusFacts.RequireNonNegative(Http3Enabled, nameof(Http3Enabled));
        ProxyStatusFacts.RequireNonNegative(QuicReady, nameof(QuicReady));
        this.Configured = Configured;
        this.Enabled = Enabled;
        this.Active = Active;
        this.Failed = Failed;
        this.Draining = Draining;
        this.Http1Enabled = Http1Enabled;
        this.Http2Enabled = Http2Enabled;
        this.Http3Enabled = Http3Enabled;
        this.QuicReady = QuicReady;
    }

    public int Configured { get; }
    public int Enabled { get; }
    public int Active { get; }
    public int Failed { get; }
    public int Draining { get; }
    public int Http1Enabled { get; }
    public int Http2Enabled { get; }
    public int Http3Enabled { get; }
    public int QuicReady { get; }
    public static ProxyListenerSubsystemSummary Unknown { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}
