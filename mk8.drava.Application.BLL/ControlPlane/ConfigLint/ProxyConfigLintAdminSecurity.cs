namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ProxyConfigLintAdminSecurity
{
    public ProxyConfigLintAdminSecurity(IEnumerable<string> Urls, bool RequireAuthentication)
    {
        ArgumentNullException.ThrowIfNull(Urls);
        this.Urls = ConfigLintList.Copy(Urls);
        this.RequireAuthentication = RequireAuthentication;
    }

    public IReadOnlyList<string> Urls { get; }
    public bool RequireAuthentication { get; }
}
