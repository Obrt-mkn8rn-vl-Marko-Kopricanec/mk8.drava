namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyProtocolSubsystemSummary
{
    public ProxyProtocolSubsystemSummary(bool clientHttp1Enabled, bool clientHttp2Enabled, bool clientHttp3Enabled, bool clientHttp3Ready, bool upstreamHttp3Configured, IReadOnlyList<string> unsupportedHttp3Features)
    {
        ClientHttp1Enabled = clientHttp1Enabled;
        ClientHttp2Enabled = clientHttp2Enabled;
        ClientHttp3Enabled = clientHttp3Enabled;
        ClientHttp3Ready = clientHttp3Ready;
        UpstreamHttp3Configured = upstreamHttp3Configured;
        UnsupportedHttp3Features = ProxyStatusList.CopyStrings(unsupportedHttp3Features, nameof(unsupportedHttp3Features));
    }

    public bool ClientHttp1Enabled { get; }
    public bool ClientHttp2Enabled { get; }
    public bool ClientHttp3Enabled { get; }
    public bool ClientHttp3Ready { get; }
    public bool UpstreamHttp3Configured { get; }
    public IReadOnlyList<string> UnsupportedHttp3Features { get; }
    public static ProxyProtocolSubsystemSummary Unknown { get; } = new(false, false, false, false, false, []);
}
