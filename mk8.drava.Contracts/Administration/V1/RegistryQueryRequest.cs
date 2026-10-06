namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class RegistryQueryRequest
{
    public string Kind { get; init; } = "instances";
    public string AfterId { get; init; } = "";
    public int Limit { get; init; } = 100;
}
