namespace Mk8.Drava.Application.INF.Dns.Management;

internal interface IZoneManagement
{
    ValueTask<ManagementReply> ExecuteAsync(ManagementRequest request, CancellationToken cancellationToken);
}
