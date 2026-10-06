namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyStatusReadinessBuilder
{
    public static (ProxyReadinessStatus Readiness, ProxySubsystemSummaries Subsystems) Build(ProxyStatusReadinessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var subsystems = new ProxySubsystemSummaries(ProxySubsystemSummaryBuilder.BuildConfig(input.HasActiveConfiguration, input.ConfigGeneration, input.ConfigurationLoadedAtUtc, input.LastListenerReloadSucceeded), ProxySubsystemSummaryBuilder.BuildListeners(input.ConfiguredListeners, input.RuntimeListeners), ProxySubsystemSummaryBuilder.BuildRoutes(input.Routes), ProxySubsystemSummaryBuilder.BuildCertificates(input.Certificates, input.ObservedAtUtc), ProxySubsystemSummaryBuilder.BuildAcme(input.Acme, input.AcmeStatuses, input.ObservedAtUtc), ProxySubsystemSummaryBuilder.BuildUpstreams(input.Upstreams), ProxySubsystemSummaryBuilder.BuildCache(input.Routes.Count(static route => route.CacheEnabled), input.CacheStatus), ProxySubsystemSummaryBuilder.BuildCircuits(input.Upstreams), ProxySubsystemSummaryBuilder.BuildLimits(input.LimitConfiguration, input.LimitRuntime), ProxySubsystemSummaryBuilder.BuildLogs(input.Log), ProxySubsystemSummaryBuilder.BuildShutdown(input.Shutdown), ProxySubsystemSummaryBuilder.BuildProtocols(input.ConfiguredListeners, input.ClientHttp3Enabled, input.ClientHttp3Ready, input.Routes));
        var readiness = ProxyReadinessEvaluator.Evaluate(new ProxyReadinessEvaluationInput(input.HasActiveConfiguration, input.ConfigGeneration, input.Shutdown.IsShuttingDown, input.LastListenerReloadFailed, input.Log.State, input.RuntimePreflight, subsystems, input.ObservedAtUtc));
        return (readiness, subsystems);
    }
}
