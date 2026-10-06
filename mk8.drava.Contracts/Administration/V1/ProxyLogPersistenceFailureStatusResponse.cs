namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyLogPersistenceFailureStatusResponse(DateTimeOffset TimestampUtc, string Category, string Reason)
{
}
