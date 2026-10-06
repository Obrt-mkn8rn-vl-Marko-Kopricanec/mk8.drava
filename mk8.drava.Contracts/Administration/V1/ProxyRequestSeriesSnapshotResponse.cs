namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyRequestSeriesSnapshotResponse(string Site, string Route, string Action, string StatusClass, long Count)
{
}
