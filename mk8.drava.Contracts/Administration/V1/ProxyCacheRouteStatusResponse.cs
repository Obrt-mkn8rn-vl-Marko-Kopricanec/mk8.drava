namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyCacheRouteStatusResponse(string RouteName, bool Enabled, long MaxEntryBytes, long MaxTotalBytes, int CurrentEntryCount, long CurrentBytes)
{
}
