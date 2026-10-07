using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using System.Buffers.Binary;
using Mk8.Drava.Application.INF.Proxy.Forwarding;

namespace Mk8.Drava.Application.INF.Proxy.Http2;
internal sealed record Http2UpstreamDataChunk(byte[] Data, bool EndStream, IReadOnlyList<ProxyHeaderField>? Trailers = null);
