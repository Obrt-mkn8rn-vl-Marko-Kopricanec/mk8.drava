using System.Net;

namespace Mk8.Drava.Application.BLL.Registry;

public sealed record InstanceIntent
{
    public InstanceIntent(RegisteredUpstreamIdentity identity, string deploymentId, string address, int port,
        string protocol, string scheme, string readinessPath, string zone, int weight, bool draining)
    {
        ArgumentNullException.ThrowIfNull(identity);
        RegistryNames.RequireLabel(deploymentId);
        RegistryNames.RequireLabel(zone);
        if (!IPAddress.TryParse(address, out var parsed) || !string.Equals(parsed.ToString(), address, StringComparison.Ordinal) ||
            port is < 1 or > 65535 || weight is < 1 or > 100_000) throw new InvalidDataException("Invalid registered endpoint.");
        if (protocol is not "http1" and not "http2" || scheme is not "http" and not "https" || (string.Equals(protocol, "http2", StringComparison.Ordinal) && !string.Equals(scheme, "https", StringComparison.Ordinal)))
            throw new InvalidDataException("Unsupported registration protocol or scheme.");
        if (string.IsNullOrEmpty(readinessPath) || readinessPath.Length > 1024 || !readinessPath.StartsWith('/') || readinessPath.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("Readiness requires a declared origin path.");
        foreach (var character in readinessPath)
            if (char.IsControl(character) || character > 127 || character == '\\' || character == '#') throw new InvalidDataException("Invalid readiness path.");
        Identity = identity;
        DeploymentId = deploymentId;
        Address = address;
        Port = port;
        Protocol = protocol;
        Scheme = scheme;
        ReadinessPath = readinessPath;
        Zone = zone;
        Weight = weight;
        Draining = draining;
    }

    public RegisteredUpstreamIdentity Identity { get; }
    public string DeploymentId { get; }
    public string Address { get; }
    public int Port { get; }
    public string Protocol { get; }
    public string Scheme { get; }
    public string ReadinessPath { get; }
    public string Zone { get; }
    public int Weight { get; }
    public bool Draining { get; }
    public InstanceIntent Drain() => new(Identity, DeploymentId, Address, Port, Protocol, Scheme, ReadinessPath, Zone, Weight, draining: true);
}
