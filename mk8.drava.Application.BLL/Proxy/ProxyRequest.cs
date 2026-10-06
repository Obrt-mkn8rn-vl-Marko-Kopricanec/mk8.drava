using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.Proxy;

public sealed record ProxyRequest(Http1RequestHead Head, string ListenerId, ForwardedHeadersPeer Peer, string ClientProtocol, long? DeclaredBodyBytes);
