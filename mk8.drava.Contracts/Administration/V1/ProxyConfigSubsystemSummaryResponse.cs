namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyConfigSubsystemSummaryResponse(bool Active, int? Generation, DateTimeOffset? LoadedAtUtc, bool? LastListenerReloadSucceeded, string? LastListenerReloadReason)
{
}
