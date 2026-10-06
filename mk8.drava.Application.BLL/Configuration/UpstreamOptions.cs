namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class UpstreamOptions
{
    public string Name { get; init; } = "";
    public string Scheme { get; init; } = "http";
    public string Protocol { get; init; } = "http1";
    public string Address { get; init; } = "";
    public int Port { get; init; }
    public int Weight { get; init; } = 1;
    public UpstreamTlsOptions UpstreamTls { get; init; } = new();
    public ProxyCircuitBreakerOptions CircuitBreaker { get; init; } = new();
}
