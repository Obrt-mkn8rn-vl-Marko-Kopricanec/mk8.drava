using System.Text;
using Google.Protobuf;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Administration;

public sealed partial class ProxyAdministrationDispatcher
{
    private ControlReply ClearCache(ControlRequest request)
    {
        RequireEmptyPayload(request);
        return ControlJson.Reply(ProxyCacheStatusResponseMapper.FromStatus(cache.Clear()));
    }

    private ControlReply QueryAcme(ControlRequest request)
    {
        RequireEmptyPayload(request);
        var value = acme.GetStatus();
        return value is null ? new ControlReply { StatusCode = 404 } : ControlJson.Reply(AcmeStatusResponseMapper.FromStatus(value));
    }

    private ControlReply ExportMetrics(ControlRequest request)
    {
        RequireEmptyPayload(request);
        if (metrics.Export() is not ProxyMetricsExportResult.ExportedResult exported) return new ControlReply { StatusCode = 404 };
        if (Encoding.UTF8.GetByteCount(exported.Content) > 4 * 1024 * 1024) throw new InvalidDataException("Metrics export exceeds its bound.");
        return new ControlReply { StatusCode = 200, ContentType = exported.ContentType, JsonPayload = ByteString.CopyFromUtf8(exported.Content) };
    }

    private async ValueTask<ControlReply> ValidateRestoreAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        RequireEmptyPayload(request);
        var result = await backup.ValidateAsync(cancellationToken).ConfigureAwait(false);
        var response = ProxyRestoreValidationResponseBodyMapper.FromResult(result);
        return ControlJson.Reply(response, response.Succeeded);
    }
}
