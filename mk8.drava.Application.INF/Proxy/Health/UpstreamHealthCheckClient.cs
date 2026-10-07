using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Proxy.Http1;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Mk8.Drava.Application.INF.Proxy.Http3;

namespace Mk8.Drava.Application.INF.Proxy.Health;
public sealed class UpstreamHealthCheckClient : IUpstreamHealthCheckClient
{
    private const int MaxHealthResponseHeadBytes = 16 * 1024;
    private readonly UpstreamConnectionFactory _connectionFactory;
    private readonly ProxyMetrics _metrics;
    public UpstreamHealthCheckClient(UpstreamConnectionFactory connectionFactory, ProxyMetrics metrics)
    {
        _connectionFactory = connectionFactory;
        _metrics = metrics;
    }

    public async ValueTask<HealthCheckSample> CheckAsync(UpstreamHealthCheckTarget target, CancellationToken cancellationToken)
    {
        var endpoint = target.TransportEndpoint;
        if (RuntimeUpstreamProtocol.IsHttp3(endpoint.Protocol))
        {
            return await CheckHttp3Async(target, cancellationToken).ConfigureAwait(false);
        }

        UpstreamTransportConnection? connection = null;
        try
        {
            connection = await _connectionFactory.ConnectAsync(endpoint, target.Timeout, cancellationToken).ConfigureAwait(false);
            var stream = connection.Stream;
            if (RuntimeUpstreamProtocol.IsHttp2(endpoint.Protocol))
            {
                return await CheckHttp2Async(target, stream, cancellationToken).ConfigureAwait(false);
            }

            var requestBytes = Encoding.ASCII.GetBytes($"GET {target.Path} HTTP/1.1\r\nHost: {endpoint.Address}\r\nConnection: close\r\n\r\n");
            await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await stream.WriteAsync(requestBytes, timeoutToken).ConfigureAwait(false), target.Timeout, ProxyTimeoutKind.DownstreamWrite, cancellationToken).ConfigureAwait(false);
            var responseHead = await ReadResponseHeadAsync(stream, target.Timeout, cancellationToken).ConfigureAwait(false);
            if (responseHead.Length == 0)
            {
                return HealthCheckSample.UnhealthyResult("empty response");
            }

            if (!Http1ResponseParser.TryParse(responseHead.Span, "GET", out var parsed, out var error))
            {
                return HealthCheckSample.UnhealthyResult($"malformed response: {error}");
            }

            return HealthCheckSample.FromHttpStatus(parsed.StatusCode);
        }
        catch (ProxyTimeoutException)
        {
            return HealthCheckSample.UnhealthyResult("timeout");
        }
        catch (Exception exception)when (exception is SocketException or IOException)
        {
            return HealthCheckSample.UnhealthyResult(exception.GetType().Name);
        }
        finally
        {
            connection?.Dispose();
        }
    }

    private async ValueTask<HealthCheckSample> CheckHttp3Async(UpstreamHealthCheckTarget target, CancellationToken cancellationToken)
    {
        var endpoint = target.TransportEndpoint;
        try
        {
            var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(target.Timeout);
            var http3 = (await Http3UpstreamConnection.ConnectAsync(endpoint, timeouts, _metrics, maxFramePayloadBytes: 16 * 1024, cancellationToken).ConfigureAwait(false));
            await using var http3Disposal = http3.ConfigureAwait(false);
            await http3.SendHeadersAsync([new ProxyHeaderField(":method", "GET"), new ProxyHeaderField(":scheme", endpoint.Scheme), new ProxyHeaderField(":authority", endpoint.Address), new ProxyHeaderField(":path", target.Path)], endStream: true, timeouts, cancellationToken).ConfigureAwait(false);
            var response = await http3.ReadResponseHeadAsync(MaxHealthResponseHeadBytes, timeouts, cancellationToken).ConfigureAwait(false);
            return HealthCheckSample.FromHttp3Status(response.StatusCode);
        }
        catch (ProxyTimeoutException)
        {
            return HealthCheckSample.UnhealthyResult("timeout");
        }
        catch (Http3UpstreamProtocolException)
        {
            return HealthCheckSample.UnhealthyResult("HTTP/3 protocol error");
        }
        catch (Exception exception)when (exception is SocketException or IOException)
        {
            return HealthCheckSample.UnhealthyResult(exception.GetType().Name);
        }
    }

    private async ValueTask<HealthCheckSample> CheckHttp2Async(UpstreamHealthCheckTarget target, Stream stream, CancellationToken cancellationToken)
    {
        var endpoint = target.TransportEndpoint;
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(target.Timeout);
        var http2 = new Http2UpstreamConnection(stream, _metrics, maxFrameSize: 16 * 1024);
        await using var http2Lifetime = http2.ConfigureAwait(false);
        await http2.InitializeAsync(timeouts, cancellationToken).ConfigureAwait(false);
        await http2.SendHeadersAsync([new ProxyHeaderField(":method", "GET"), new ProxyHeaderField(":scheme", endpoint.Scheme), new ProxyHeaderField(":authority", endpoint.Address), new ProxyHeaderField(":path", target.Path)], endStream: true, timeouts, cancellationToken).ConfigureAwait(false);
        var response = await http2.ReadResponseHeadAsync(MaxHealthResponseHeadBytes, timeouts, cancellationToken).ConfigureAwait(false);
        return HealthCheckSample.FromHttp2Status(response.StatusCode);
    }

    private static async ValueTask<ReadOnlyMemory<byte>> ReadResponseHeadAsync(Stream stream, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxHealthResponseHeadBytes);
        var total = 0;
        try
        {
            while (total < MaxHealthResponseHeadBytes)
            {
                var bytesRead = await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await stream.ReadAsync(buffer.AsMemory(total, MaxHealthResponseHeadBytes - total), timeoutToken).ConfigureAwait(false), timeout, ProxyTimeoutKind.UpstreamResponseHead, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return ReadOnlyMemory<byte>.Empty;
                }

                total += bytesRead;
                var headLength = Http1HeadTerminator.FindLength(buffer.AsSpan(0, total));
                if (headLength > 0)
                {
                    return buffer.AsMemory(0, headLength).ToArray();
                }
            }

            return ReadOnlyMemory<byte>.Empty;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
