namespace Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
public sealed record UpgradeRequestInfo(string Protocol, bool IsWebSocket, string? WebSocketKey);
