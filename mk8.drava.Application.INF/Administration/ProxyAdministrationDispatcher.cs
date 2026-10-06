using System.Text.Json;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Status;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Administration;

public sealed partial class ProxyAdministrationDispatcher(
    ProxyStatusAdministrationService status, ProxyConfigurationAdministrationService configuration,
    ProxyConfigurationReadAdministrationService<ProxyConfigurationProjection> reads,
    ProxyConfigurationReloadAdministrationService<ProxyConfigurationProjection> reloads,
    ProxyConfigLintAdministrationService lint, ProxyRouteDiagnosticsAdministrationService routes,
    ProxyCacheAdministrationService cache, ProxyDiagnosticsAdministrationService diagnostics,
    ProxyAdminAuditAdministrationService audit, ProxyAcmeAdministrationService acme,
    ProxyMetricsAdministrationService metrics, ProxyBackupAdministrationService backup, NoConfAdministration? drava = null)
{
    public async ValueTask<ControlReply> ExecuteAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return request.Operation switch
            {
                ControlOperation.RuntimeStatus => EmptyPayload(request, ControlJson.Reply(ProxyStatusResponseMapper.FromBusinessResponse(status.GetStatus()))),
                ControlOperation.ConfigurationQuery => QueryConfiguration(request),
                ControlOperation.ConfigurationNormalize => Normalize(request),
                ControlOperation.ConfigurationValidate => await ValidateAsync(request, cancellationToken).ConfigureAwait(false),
                ControlOperation.ConfigurationReload => await ReloadAsync(request, cancellationToken).ConfigureAwait(false),
                ControlOperation.ConfigurationLint => Lint(request),
                ControlOperation.RouteDryRun => Match(request),
                ControlOperation.CacheQuery => EmptyPayload(request, ControlJson.Reply(ProxyCacheStatusResponseMapper.FromStatus(cache.GetStatus()))),
                ControlOperation.CacheClear => ClearCache(request),
                ControlOperation.DiagnosticsQuery => ControlJson.Reply(ProxyRecentRequestDiagnosticEventResponseMapper.FromEvents(diagnostics.Recent(RecentLimit(request)))),
                ControlOperation.AuditQuery => ControlJson.Reply(ProxyAdminAuditEventResponseMapper.FromEvents(audit.Recent(RecentLimit(request)))),
                ControlOperation.AcmeQuery => QueryAcme(request),
                ControlOperation.MetricsQuery => EmptyPayload(request, ControlJson.Reply(ProxyStatusResponseMapper.FromBusinessResponse(status.GetStatus()).Metrics)),
                ControlOperation.MetricsExport => ExportMetrics(request),
                ControlOperation.BackupQuery => EmptyPayload(request, ControlJson.Reply(ProxyBackupManifestResponseMapper.FromManifest(backup.CreateManifest()))),
                ControlOperation.RestoreValidate => await ValidateRestoreAsync(request, cancellationToken).ConfigureAwait(false),
                ControlOperation.RegistryQuery or ControlOperation.PolicyQuery or ControlOperation.PolicyUpdate or ControlOperation.PolicyRollback or ControlOperation.NodeRevoke or ControlOperation.InstanceRevoke
                    => drava is null ? new ControlReply { StatusCode = 503, SafeReason = "Controller administration is unavailable." } : await drava.ExecuteAsync(request, cancellationToken).ConfigureAwait(false),
                _ => new ControlReply { StatusCode = 501, SafeReason = "Control operation is not supported." },
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or JsonException)
        {
            return drava is { StorageHealthy: false } ? new ControlReply { StatusCode = 503, SafeReason = "Control authority is unavailable." }
                : new ControlReply { StatusCode = 400, SafeReason = "Control request failed validation." };
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return new ControlReply { StatusCode = 503, SafeReason = "Control authority is unavailable." };
        }
    }

    private static int RecentLimit(ControlRequest request)
    {
        var limit = request.JsonPayload.Length == 0 ? 50 : ControlJson.Read<RecentItemsRequest>(request.JsonPayload).Limit;
        if (limit is < 1 or > 1000) throw new InvalidDataException("Recent query limit is out of bounds.");
        return limit;
    }

    private static void RequireEmptyPayload(ControlRequest request)
    {
        if (request.JsonPayload.Length != 0) throw new InvalidDataException("Control operation does not accept a payload.");
    }

    private static ControlReply EmptyPayload(ControlRequest request, ControlReply reply)
    {
        RequireEmptyPayload(request);
        return reply;
    }
}
