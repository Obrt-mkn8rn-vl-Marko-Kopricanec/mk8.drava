using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.INF.Acme;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
using Mk8.Drava.Application.BLL.ControlPlane.Status;
using Mk8.Drava.Application.INF.Proxy.RuntimeGuards;
using Mk8.Drava.Application.INF.Runtime;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.DAL.DataDirectory;
using Microsoft.Extensions.Options;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.Proxy;
using Mk8.Drava.Application.INF.Proxy.Exchange;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
using Mk8.Drava.Application.INF.Proxy.Caching;
using Mk8.Drava.Application.INF.Proxy.ConfigLint;
using Mk8.Drava.Application.INF.Proxy.RouteDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Observability;

namespace Mk8.Drava.Application.Hosting;
internal static partial class ProxyServiceCollectionExtensions
{
    private static void AddProxyForwardingServices(this IServiceCollection services)
    {
        services.AddSingleton<IRouteMatcher, SingleUpstreamRouteMatcher>();
        services.AddSingleton<IUpstreamSelector, RoundRobinUpstreamSelector>();
        services.AddSingleton<Mk8.Drava.Application.BLL.Registry.DestinationAvailabilityStore>();
        services.AddSingleton<IUpstreamReservationSelector, PolicyUpstreamSelector>();
        services.AddSingleton<UpstreamConnectionFactory>();
        services.AddSingleton<UpstreamConnectionPool>();
        services.AddSingleton<IUpstreamConnectionPruner>(static services => services.GetRequiredService<UpstreamConnectionPool>());
        services.AddSingleton<Http3UpstreamConnectionPool>();
        services.AddSingleton<UpstreamHealthCheckClient>();
        services.AddSingleton<IUpstreamHealthCheckClient>(static services => services.GetRequiredService<UpstreamHealthCheckClient>());
        services.AddSingleton<IUpstreamHealthCheckEventSink, UpstreamHealthCheckLogger>();
        services.AddSingleton<IUpstreamHealthCheckTargetSource, ProxyConfigurationUpstreamHealthCheckTargetSource>();
        services.AddSingleton<UpstreamHealthCheckCoordinator>();
        services.AddSingleton<HopByHopHeaderPolicy>();
        services.AddSingleton<ForwardedHeadersPolicy>();
        services.AddSingleton<UpgradeRequestPolicy>();
        services.AddSingleton<ProxyRouteActionPolicy>();
        services.AddSingleton<PathRewritePolicy>();
        services.AddSingleton<Http3AltSvcPolicy>();
        services.AddSingleton<TunnelRelay>();
        services.AddSingleton<ProxyForwarder>();
        services.AddSingleton<UpgradeForwarder>();
    }

    private static void AddProxyAcmeServices(this IServiceCollection services)
    {
        services.AddSingleton<AcmeChallengeStore>();
        services.AddSingleton<AcmeHttp01ChallengeResponder>();
        services.AddSingleton<AcmeCertificateStatusStore>();
        services.AddSingleton<IAcmeCertificateMaterialWriter, AcmeCertificateMaterialWriter>();
        services.AddSingleton<IAcmeCertificateRenewalEventSink, AcmeCertificateRenewalLogger>();
        services.AddSingleton<IProxyAcmeStatusConfigurationSource, ProxyAcmeStatusConfigurationSource>();
        services.AddSingleton<IProxyAcmeCertificateLifecycleStatusSource, ProxyAcmeCertificateLifecycleStatusSource>();
        services.AddSingleton<IProxyAcmeStatusSnapshotReader, ProxyAcmeStatusSnapshotReader>();
        services.AddSingleton<ProxyAcmeAdministrationService>();
        services.AddSingleton<IAcmeCertificateIssuer, DisabledAcmeCertificateIssuer>();
        services.AddSingleton<IAcmeRenewalScheduleInputSource, ProxyConfigurationAcmeRenewalScheduleInputSource>();
        services.AddSingleton<IAcmeRenewalConfigurationSource, ProxyConfigurationAcmeRenewalConfigurationSource>();
        services.AddSingleton<IAcmeCertificateActivator, ProxyConfigurationAcmeCertificateActivator>();
        services.AddSingleton<AcmeRenewalSchedulePolicy>();
        services.AddSingleton<AcmeCertificateManager>();
    }

    private static void AddProxyHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<UpstreamHealthCheckService>();
    }

    private static void AddProxyRuntimeServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ProxyListenerReloadPlanner>();
        services.AddSingleton<IRuntimeHttp3PlatformSupportSource, SystemRuntimeHttp3PlatformSupportSource>();
        services.AddSingleton<IProxyListenerReloadApplier>(static _ => new GatewayPlanCoordinator());
        services.AddSingleton<ProxyAdmissionController>();
        services.AddSingleton<IProxyRuntimeDirectoryProbe, ProxyRuntimeDirectoryProbe>();
        services.AddSingleton<ProxyRuntimePreflightService>();
        services.AddSingleton<IProxyStatusConfigurationSource>(static services => services.GetRequiredService<ProxyConfigurationStore>());
        services.AddSingleton<IProxyStatusMetricsSource>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyStatusRuntimePreflightSource>(static services => services.GetRequiredService<ProxyRuntimePreflightService>());
        services.AddSingleton<IProxyStatusInputReader, ProxyStatusInputReader>();
        services.AddSingleton<IProxyStatusOperations, ProxyStatusOperations>();
        services.AddSingleton<ProxyStatusAdministrationService>();
        services.AddSingleton<CircuitBreakerStore>();
        services.AddSingleton<ProxyShutdownCoordinator>();
        services.AddSingleton<ClientRateLimiter>();
        services.AddSingleton<ProxyRuntimeState>();
        services.AddSingleton<IProxyStatusRuntimeStateSource>(static services => services.GetRequiredService<ProxyRuntimeState>());
        services.AddSingleton<IHttp3AltSvcRuntimeListenerSource>(static services => services.GetRequiredService<ProxyRuntimeState>());
        services.AddSingleton<UpstreamHealthStore>();
        services.AddSingleton<IProxyStatusUpstreamHealthSource>(static services => services.GetRequiredService<UpstreamHealthStore>());
        services.AddSingleton<IProxyStatusUpstreamHealthReader, ProxyStatusUpstreamHealthReader>();
    }

    private static void AddProxyConfigurationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MdravaDataDirectoryOptions>().Bind(configuration.GetSection(MdravaDataDirectoryOptions.SectionName));
        services.AddSingleton<IMdravaDataDirectoryProvider>(static services => new MdravaDataDirectoryProvider(services.GetRequiredService<IOptions<MdravaDataDirectoryOptions>>().Value));
        services.AddSingleton<IValidateOptions<ProxyOptions>, ProxyOptionsValidator>();
        services.AddSingleton<ProxyDataDirectoryBootstrapper>();
        services.AddSingleton<SiteConfigurationParser>();
        services.AddSingleton<IProxyConfigurationNormalizeSiteParser, ProxyConfigurationNormalizeSiteParser>();
        services.AddSingleton<ProxyConfigurationStore>();
        services.AddSingleton<IProxyConfigurationStore>(static services => services.GetRequiredService<ProxyConfigurationStore>());
        services.AddSingleton<IProxyActiveConfigurationSnapshotReader>(static services => services.GetRequiredService<ProxyConfigurationStore>());
        services.AddSingleton<IProxyActiveConfigurationSnapshotWriter>(static services => services.GetRequiredService<ProxyConfigurationStore>());
        services.AddSingleton<IProxyActiveConfigurationVersionReader>(static services => services.GetRequiredService<ProxyConfigurationStore>());
        services.AddSingleton<ProxyConfigurationLoader>();
        services.AddSingleton<IProxyConfigurationLoader>(static services => services.GetRequiredService<ProxyConfigurationLoader>());
        services.AddSingleton<IProxyRestoreConfigurationValidator>(static services => services.GetRequiredService<ProxyConfigurationLoader>());
        services.AddSingleton<IProxyConfigurationReloadEventSink, ProxyConfigurationReloadLogger>();
        services.AddSingleton<IProxyConfigurationHttp3ProjectionSource, ProxyConfigurationHttp3ProjectionSource>();
        services.AddSingleton<ProxyConfigurationReloadService>();
        services.AddSingleton<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>(static services => services.GetRequiredService<ProxyConfigurationReloadService>());
        services.AddSingleton<IProxyConfigurationNormalizeOperations, ProxyConfigurationNormalizer>();
        services.AddSingleton<IProxyConfigurationValidationOperations>(static services => services.GetRequiredService<ProxyConfigurationReloadService>());
        services.AddSingleton<ProxyConfigurationAdministrationService>();
        services.AddSingleton<ProxyConfigurationReloadAdministrationService<ProxyConfigurationProjection>>();
        services.AddSingleton<IProxyConfigurationReadProjectionSource<ProxyConfigurationProjection>, ProxyConfigurationReadProjectionSource>();
        services.AddSingleton<IProxyConfigurationReadOperations<ProxyConfigurationProjection>, ProxyConfigurationReadOperations<ProxyConfigurationProjection>>();
        services.AddSingleton<ProxyConfigurationReadAdministrationService<ProxyConfigurationProjection>>();
        services.AddSingleton<IProxyAdminUrlPolicy, ProxyAdminUrlPolicy>();
        services.AddSingleton<IProxyEndpointAddressPolicy, ProxyEndpointAddressPolicy>();
        services.AddSingleton<IProxyRelativeStoragePathPolicy, ProxyRelativeStoragePathPolicy>();
        services.AddSingleton<IProxyUrlSyntaxPolicy, ProxyUrlSyntaxPolicy>();
        services.AddSingleton<ProxyForwardedHeadersAddressPolicy>();
        services.AddSingleton<IProxyTrustedProxyPolicy>(static services => services.GetRequiredService<ProxyForwardedHeadersAddressPolicy>());
        services.AddSingleton<IForwardedHeadersAddressPolicy>(static services => services.GetRequiredService<ProxyForwardedHeadersAddressPolicy>());
        services.AddSingleton<IProxyDataDirectoryPathSafety, ProxyDataDirectoryPathSafety>();
    }

    public static IServiceCollection AddProxyApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProxyConfigurationServices(configuration);
        services.AddProxyMetricsAndLoggingServices();
        services.AddProxyAdministrationServices();
        services.AddProxyAcmeServices();
        services.AddProxyRuntimeServices();
        services.AddProxyForwardingServices();
        services.AddProxyHostedServices();
        services.AddSingleton<IProxyRequestObserver, ProxyRequestObserver>();
        services.AddSingleton(CreatePipelineServices);
        services.AddSingleton<ProxyRequestPipeline>();
        return services;
    }

    private static void AddProxyAdministrationServices(this IServiceCollection services)
    {
        services.AddSingleton<IProxyBackupFileSystem, ProxyBackupFileSystem>();
        services.AddSingleton<ProxyBackupService>();
        services.AddSingleton<IProxyBackupOperations>(static services => services.GetRequiredService<ProxyBackupService>());
        services.AddSingleton<ProxyBackupAdministrationService>();
        services.AddSingleton<IProxyConfigLintActiveConfigurationSource, ProxyConfigLintActiveConfigurationSource>();
        services.AddSingleton<IProxyConfigLintSubmittedConfigurationSource, ProxyConfigLintSubmittedConfigurationSource>();
        services.AddSingleton<IProxyConfigLintRuntimeStateSource, ProxyConfigLintRuntimeStateSource>();
        services.AddSingleton<IProxyConfigLintMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyConfigLintSourceNameFormatter, ProxyConfigLintSourceNameFormatter>();
        services.AddSingleton<ConfigLintService>();
        services.AddSingleton<IProxyConfigLintOperations>(static services => services.GetRequiredService<ConfigLintService>());
        services.AddSingleton<ProxyConfigLintAdministrationService>();
        services.AddSingleton<IProxyRouteDiagnosticsConfigurationSource, ProxyRouteDiagnosticsConfigurationSource>();
        services.AddSingleton<IProxyRouteDiagnosticsMatcher, ProxyRouteDiagnosticsMatcher>();
        services.AddSingleton<IProxyRouteDiagnosticsActionPolicy, ProxyRouteDiagnosticsActionPolicyAdapter>();
        services.AddSingleton<IProxyRouteDiagnosticsPathRewritePolicy, ProxyRouteDiagnosticsPathRewritePolicyAdapter>();
        services.AddSingleton<IProxyRouteDiagnosticsMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyClientAddressSyntaxPolicy, ProxyClientAddressSyntaxPolicy>();
        services.AddSingleton<RouteMatchDiagnosticsService>();
        services.AddSingleton<IProxyRouteDiagnosticsOperations>(static services => services.GetRequiredService<RouteMatchDiagnosticsService>());
        services.AddSingleton<ProxyRouteDiagnosticsAdministrationService>();
        services.AddSingleton<RequestIdGenerator>();
        services.AddSingleton<ResponseCacheStore>();
        services.AddSingleton<IProxyCacheControl>(static services => services.GetRequiredService<ResponseCacheStore>());
        services.AddSingleton<IProxyCacheStatusConfigurationSource, ProxyCacheStatusConfigurationSource>();
        services.AddSingleton<IProxyCacheRuntimeStatusSource, ProxyCacheRuntimeStatusSource>();
        services.AddSingleton<ProxyCacheStatusReader>();
        services.AddSingleton<IProxyCacheStatusReader>(static services => services.GetRequiredService<ProxyCacheStatusReader>());
        services.AddSingleton<ProxyCacheAdministrationService>();
        services.AddSingleton<RecentRequestDiagnosticsStore>();
        services.AddSingleton<IProxyRequestDiagnosticsSource>(static services => services.GetRequiredService<RecentRequestDiagnosticsStore>());
        services.AddSingleton<IProxyRequestDiagnosticsReader, ProxyRequestDiagnosticsReader>();
        services.AddSingleton<IProxyRequestIdRuntimeIdentitySource, SystemRequestIdRuntimeIdentitySource>();
        services.AddSingleton<ProxyDiagnosticsAdministrationService>();
        services.AddProxyAuditServices();
    }

    private static void AddProxyAuditServices(this IServiceCollection services)
    {
        services.AddSingleton<AdminAuditStore>();
        services.AddSingleton<IProxyAdminAuditReader>(static services => services.GetRequiredService<AdminAuditStore>());
        services.AddSingleton<IProxyAdminAuditRecorder>(static services => services.GetRequiredService<AdminAuditStore>());
        services.AddSingleton<ProxyAdminAuditAdministrationService>();
        services.AddSingleton<IProxyAdminSecurityOptionsReader, ProxyAdminSecurityOptionsReader>();
        services.AddSingleton<IProxyAdminAuthenticationEventSink, AdminAuthenticationLogger>();
        services.AddSingleton<ProxyAdminAuthenticationService>();
    }

    private static void AddProxyMetricsAndLoggingServices(this IServiceCollection services)
    {
        services.AddSingleton<ProxyMetrics>();
        services.AddSingleton<IProxyUpstreamSelectionMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyCircuitBreakerMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyUpstreamHealthMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyHttp3AltSvcMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyAccessLogMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyRequestDiagnosticsMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyRequestIdMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyRateLimitMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyAdmissionMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyConfigurationReloadMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyHealthCheckMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyAcmeMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<IProxyAdminAuthenticationMetricsSink>(static services => services.GetRequiredService<ProxyMetrics>());
        services.AddSingleton<PrometheusMetricsExporter>();
        services.AddSingleton<IProxyMetricsExportConfigurationSource, ProxyConfigurationMetricsExportConfigurationSource>();
        services.AddSingleton<IProxyMetricsExportAvailabilityReader, ProxyMetricsExportAvailabilityReader>();
        services.AddSingleton<ProxyMetricsExportAvailabilityService>();
        services.AddSingleton<IProxyMetricsExportInputSource, ProxyMetricsExportInputSource>();
        services.AddSingleton<IProxyMetricsExportProvider, ProxyMetricsExportProvider>();
        services.AddSingleton<ProxyMetricsAdministrationService>();
        services.AddSingleton<IProxyLogPersistenceSettingsSource, ProxyConfigurationLogPersistenceSettingsSource>();
        services.AddSingleton<IProxyLogPersistenceSettingsReader, ProxyLogPersistenceSettingsReader>();
        services.AddSingleton<ProxyPersistentLogWriter>();
        services.AddSingleton<IProxyLogPersistenceStore>(static services => services.GetRequiredService<ProxyPersistentLogWriter>());
        services.AddSingleton<AccessLogEmitter>();
    }
    private static ProxyPipelineServices CreatePipelineServices(IServiceProvider services) => new()
    {
        Configuration = services.GetRequiredService<IProxyActiveConfigurationSnapshotReader>(),
        RouteMatcher = services.GetRequiredService<IRouteMatcher>(),
        Selector = services.GetRequiredService<IUpstreamReservationSelector>(),
        Health = services.GetRequiredService<UpstreamHealthStore>(),
        Circuits = services.GetRequiredService<CircuitBreakerStore>(),
        ForwardedHeaders = services.GetRequiredService<ForwardedHeadersPolicy>(),
        RouteActions = services.GetRequiredService<ProxyRouteActionPolicy>(),
        PathRewrite = services.GetRequiredService<PathRewritePolicy>(),
        Upgrades = services.GetRequiredService<UpgradeRequestPolicy>(),
        Cache = services.GetRequiredService<ResponseCacheStore>(),
        Challenges = services.GetRequiredService<AcmeHttp01ChallengeResponder>(),
        RateLimiter = services.GetRequiredService<ClientRateLimiter>(),
        Metrics = services.GetRequiredService<ProxyMetrics>(),
        RequestIds = services.GetRequiredService<RequestIdGenerator>(),
        Observer = services.GetRequiredService<IProxyRequestObserver>(),
        Clock = services.GetRequiredService<TimeProvider>(),
    };
}
