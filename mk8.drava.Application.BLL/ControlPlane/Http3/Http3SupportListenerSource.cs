using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public sealed record Http3SupportListenerSource(bool Configured, bool EnabledForTraffic, string EnablementLevel, bool AltSvcEnabled, int AltSvcMaxAgeSeconds, string? QuicListenerIdentity);
