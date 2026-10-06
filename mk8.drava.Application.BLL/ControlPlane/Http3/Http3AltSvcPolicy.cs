using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public sealed class Http3AltSvcPolicy
{
    private readonly IHttp3AltSvcRuntimeListenerSource _runtimeListeners;
    private readonly IProxyHttp3AltSvcMetricsSink _metrics;
    public Http3AltSvcPolicy(IHttp3AltSvcRuntimeListenerSource runtimeListeners, IProxyHttp3AltSvcMetricsSink metrics)
    {
        _runtimeListeners = runtimeListeners;
        _metrics = metrics;
    }

    public Http3AltSvcHeaderResult CreateHeader(Http3AltSvcListenerInput listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (!Http3AltSvcListenerPolicy.IsEnabled(listener))
        {
            _metrics.Http3AltSvcSuppressed();
            return Http3AltSvcHeaderResult.Suppressed;
        }

        if (!Http3AltSvcListenerPolicy.HasActiveQuicListener(listener, _runtimeListeners.ReadRuntimeListeners()))
        {
            _metrics.Http3AltSvcSuppressed();
            return Http3AltSvcHeaderResult.Suppressed;
        }

        _metrics.Http3AltSvcEmitted();
        return Http3AltSvcHeaderResult.Emitted(new ProxyHeaderField("Alt-Svc", $"h3=\":{listener.Port}\"; ma={listener.AltSvcMaxAgeSeconds}"));
    }

    public static IReadOnlyList<ProxyHeaderField> ApplyHeader(IReadOnlyList<ProxyHeaderField> headers, Http3AltSvcHeaderResult result)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(result);
        var projected = headers.Where(static header => !string.Equals(header.Name, "alt-svc", StringComparison.OrdinalIgnoreCase)).ToList();
        if (result is Http3AltSvcHeaderResult.EmittedResult emitted)
        {
            projected.Add(emitted.Header);
        }

        return projected;
    }
}
