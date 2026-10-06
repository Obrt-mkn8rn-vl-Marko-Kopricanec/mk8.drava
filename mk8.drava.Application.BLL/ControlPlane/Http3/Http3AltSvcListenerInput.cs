using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public sealed record Http3AltSvcListenerInput(bool EnabledForTraffic, string EnablementLevel, bool AltSvcEnabled, int AltSvcMaxAgeSeconds, int Port, string? QuicListenerIdentity);
