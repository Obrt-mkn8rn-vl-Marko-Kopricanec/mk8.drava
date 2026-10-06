namespace Mk8.Drava.Application.BLL.NoConf;

public sealed record NoConfPolicyView(PolicyRevision? Accepted, long AppliedRevision, int RuntimeVersion, long RegistryRevision,
    string Failure, CompiledNoConfSnapshot? Compiled, IReadOnlyList<PolicyRevision> History);
