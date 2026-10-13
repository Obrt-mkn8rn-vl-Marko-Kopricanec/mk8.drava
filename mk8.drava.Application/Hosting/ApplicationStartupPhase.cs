namespace Mk8.Drava.Application.Hosting;

internal enum ApplicationStartupPhase
{
    BootstrapRead,
    PrivateStateOpen,
    ServiceConfiguration,
    RegistrationStateOpen,
    ServiceProviderBuild,
    RuntimeConfigurationLoad,
    ServingServicesInitialize,
    AutomaticRoutesInitialize,
    ListenerStart,
    HostStarted,
    SiteWorkspaceOpen,
    SiteIssuerCreate,
    SiteNodeEnroll,
    SiteConfigurationProtect,
    SitePublish,
    SiteInitializationComplete,
}
