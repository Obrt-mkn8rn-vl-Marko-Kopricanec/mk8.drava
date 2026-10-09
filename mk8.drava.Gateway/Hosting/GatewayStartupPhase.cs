namespace Mk8.Drava.Gateway.Hosting;

internal enum GatewayStartupPhase
{
    BootstrapRead,
    PrivateChannelOpen,
    CachedMaterialRestore,
    ServiceConfiguration,
    ServiceProviderBuild,
    ListenerStart,
    HostStarted,
}
