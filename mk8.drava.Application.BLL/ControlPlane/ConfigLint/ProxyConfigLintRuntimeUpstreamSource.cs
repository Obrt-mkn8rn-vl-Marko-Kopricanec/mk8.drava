using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ProxyConfigLintRuntimeUpstreamSource
{
    public ProxyConfigLintRuntimeUpstreamSource(string name, string scheme, string protocol, bool tlsValidateCertificate, bool circuitBreakerEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        Name = name;
        Scheme = scheme;
        Protocol = protocol;
        TlsValidateCertificate = tlsValidateCertificate;
        CircuitBreakerEnabled = circuitBreakerEnabled;
    }

    public string Name { get; }
    public string Scheme { get; }
    public string Protocol { get; }
    public bool TlsValidateCertificate { get; }
    public bool CircuitBreakerEnabled { get; }
}
