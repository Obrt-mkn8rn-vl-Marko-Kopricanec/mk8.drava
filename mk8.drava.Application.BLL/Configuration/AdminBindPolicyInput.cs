namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record AdminBindPolicyInput
{
    public AdminBindPolicyInput(IReadOnlyList<AdminBindCandidate> Candidates, AdminStartupSecurityOptions StartupSecurity)
    {
        this.Candidates = RuntimeList.Copy(Candidates);
        ArgumentNullException.ThrowIfNull(StartupSecurity);
        this.StartupSecurity = StartupSecurity;
    }

    public IReadOnlyList<AdminBindCandidate> Candidates { get; }
    public AdminStartupSecurityOptions StartupSecurity { get; init; }
}
