using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.IntegrationTests;

internal sealed record DevelopmentFlowTimeoutSettings
{
    public ProxyTimeoutOptions Global { get; init; } = new();
    public ProxyRouteOverrideOptions Events { get; init; } = new();
}
