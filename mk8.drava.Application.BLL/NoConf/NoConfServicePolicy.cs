namespace Mk8.Drava.Application.BLL.NoConf;

public sealed record NoConfServicePolicy
{
    public string ServiceId { get; init; } = "";
    public NoConfPolicyPatch Profile { get; init; } = new();
    public NoConfPolicyPatch Route { get; init; } = new();
}
