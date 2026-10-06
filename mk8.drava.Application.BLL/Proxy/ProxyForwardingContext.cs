using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.Proxy;

public sealed record ProxyForwardingContext(Http1RequestHead Head, RuntimeRoute Route, RuntimeUpstream Upstream,
    RuntimeListener Listener, RuntimeTimeouts Timeouts, RuntimeConnectionLimits ConnectionLimits, RuntimeLimits Limits,
    string UpstreamTarget, ForwardedHeadersContext ForwardedHeaders, string RequestId, bool SuppressFailureResponse);
