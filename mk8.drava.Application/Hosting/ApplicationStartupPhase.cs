namespace Mk8.Drava.Application.Hosting;

internal enum ApplicationStartupPhase
{
    BootstrapRead,
    PrivateStateOpen,
    ServiceConfiguration,
    RegistrationStateOpen,
    ServiceProviderBuild,
    RuntimeConfigurationLoad,
    AutomaticRoutesInitialize,
    ListenerStart,
    HostStarted,
}
