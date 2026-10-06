namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class PolicyQueryRequest
{
    public bool IncludeHistory { get; init; }
    public string AfterServiceId { get; init; } = "";
    public int Limit { get; init; } = 100;
}
