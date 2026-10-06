using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed record TcpAlpnAdvertisementInput(bool Http1Enabled, bool Http2Enabled);
