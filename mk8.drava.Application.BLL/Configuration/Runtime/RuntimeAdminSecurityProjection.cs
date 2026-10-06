namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeAdminSecurityProjection
{
    public RuntimeAdminSecurityProjection(IReadOnlyList<string> Urls, bool RequireAuthentication, bool HasConfiguredToken, string? Token, string TokenEnvironmentVariable, string TokenSource, int RecentAuditCapacity)
    {
        RuntimeAdminSecurityFacts.Validate(TokenEnvironmentVariable, TokenSource, RecentAuditCapacity);
        this.Urls = RuntimeList.Copy(Urls);
        this.RequireAuthentication = RequireAuthentication;
        this.HasConfiguredToken = HasConfiguredToken;
        this.Token = Token;
        this.TokenEnvironmentVariable = TokenEnvironmentVariable;
        this.TokenSource = TokenSource;
        this.RecentAuditCapacity = RecentAuditCapacity;
    }

    public IReadOnlyList<string> Urls { get; }
    public bool RequireAuthentication { get; }
    public bool HasConfiguredToken { get; }
    public string? Token { get; }
    public string TokenEnvironmentVariable { get; }
    public string TokenSource { get; }
    public int RecentAuditCapacity { get; }
}
