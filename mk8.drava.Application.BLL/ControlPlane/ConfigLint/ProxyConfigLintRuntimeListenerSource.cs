using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ProxyConfigLintRuntimeListenerSource
{
    public ProxyConfigLintRuntimeListenerSource(string name, string address, int port, bool enabled, string transport, bool http3Configured, bool http3EnabledForTraffic, string http3DisabledReason, string http3EnablementLevel, bool http3AltSvcEnabled, string? quicIdentityKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentNullException.ThrowIfNull(http3DisabledReason);
        ArgumentException.ThrowIfNullOrWhiteSpace(http3EnablementLevel);
        Name = name;
        Address = address;
        Port = port;
        Enabled = enabled;
        Transport = transport;
        Http3Configured = http3Configured;
        Http3EnabledForTraffic = http3EnabledForTraffic;
        Http3DisabledReason = http3DisabledReason;
        Http3EnablementLevel = http3EnablementLevel;
        Http3AltSvcEnabled = http3AltSvcEnabled;
        QuicIdentityKey = quicIdentityKey;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public bool Enabled { get; }
    public string Transport { get; }
    public bool Http3Configured { get; }
    public bool Http3EnabledForTraffic { get; }
    public string Http3DisabledReason { get; }
    public string Http3EnablementLevel { get; }
    public bool Http3AltSvcEnabled { get; }
    public string? QuicIdentityKey { get; }
}
