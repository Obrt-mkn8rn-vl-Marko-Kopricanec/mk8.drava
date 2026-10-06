namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeMaintenanceResponse(bool Enabled, int? RetryAfterSeconds, string ContentType, string Body)
{
}
