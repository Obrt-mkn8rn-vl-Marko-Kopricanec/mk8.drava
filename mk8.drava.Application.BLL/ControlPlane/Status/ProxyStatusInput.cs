using System.Collections.ObjectModel;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyStatusInput
{
    public ProxyStatusInput(ProxyStatusRuntimeSummary Runtime, ProxyStatusConfigurationSummary? Configuration, ProxyMetricsSnapshot Metrics, IReadOnlyList<ProxyUpstreamStatus> Upstreams, RuntimeHttp3SupportProjection Http3, ProxyLogPersistenceStatus LogPersistence, ProxyCacheStatus? CacheStatus, IReadOnlyList<AcmeCertificateLifecycleStatus> AcmeStatuses, ProxyRuntimePreflightStatus RuntimePreflight, DateTimeOffset ObservedAtUtc, ProxyStatusReadinessInput Readiness, ConfigLintStatus ConfigLint)
    {
        ArgumentNullException.ThrowIfNull(Runtime);
        ArgumentNullException.ThrowIfNull(Upstreams);
        ArgumentNullException.ThrowIfNull(AcmeStatuses);
        ArgumentNullException.ThrowIfNull(Readiness);
        this.Runtime = Runtime;
        this.Configuration = Configuration;
        this.Metrics = Metrics;
        this.Upstreams = ProxyStatusList.Copy(Upstreams);
        this.Http3 = Http3;
        this.LogPersistence = LogPersistence;
        this.CacheStatus = CacheStatus;
        this.AcmeStatuses = ProxyStatusList.Copy(AcmeStatuses);
        this.RuntimePreflight = RuntimePreflight;
        this.ObservedAtUtc = ObservedAtUtc;
        this.Readiness = Readiness;
        this.ConfigLint = ConfigLint;
    }

    public ProxyStatusRuntimeSummary Runtime { get; }
    public ProxyStatusConfigurationSummary? Configuration { get; }
    public ProxyMetricsSnapshot Metrics { get; }
    public IReadOnlyList<ProxyUpstreamStatus> Upstreams { get; }
    public RuntimeHttp3SupportProjection Http3 { get; }
    public ProxyLogPersistenceStatus LogPersistence { get; }
    public ProxyCacheStatus? CacheStatus { get; }
    public IReadOnlyList<AcmeCertificateLifecycleStatus> AcmeStatuses { get; }
    public ProxyRuntimePreflightStatus RuntimePreflight { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public ProxyStatusReadinessInput Readiness { get; }
    public ConfigLintStatus ConfigLint { get; }
}
