namespace Mk8.Drava.Application.BLL.NoConf;

// These are configuration inputs. The compiler copies them into immutable runtime values before publication.
public sealed record NoConfPolicy
{
    public int Version { get; init; } = 1;
    public string Mode { get; init; } = "hybrid";
    public NoConfPolicyPatch Global { get; init; } = new();
    public NoConfPolicyPatch Site { get; init; } = new();
    public IReadOnlyList<NoConfServicePolicy> Services { get; init; } = [];
}
