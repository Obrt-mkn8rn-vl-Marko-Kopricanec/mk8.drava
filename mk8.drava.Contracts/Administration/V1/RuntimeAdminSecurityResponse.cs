namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeAdminSecurityResponse
{
    public RuntimeAdminSecurityResponse(IReadOnlyList<string> urls, bool requireAuthentication, bool hasConfiguredToken, string? token, string tokenEnvironmentVariable, string tokenSource, int recentAuditCapacity)
    {
        Urls = ApiResponseList.Copy(urls);
        RequireAuthentication = requireAuthentication;
        HasConfiguredToken = hasConfiguredToken;
        Token = token;
        TokenEnvironmentVariable = tokenEnvironmentVariable;
        TokenSource = tokenSource;
        RecentAuditCapacity = recentAuditCapacity;
    }

    public IReadOnlyList<string> Urls { get; }
    public bool RequireAuthentication { get; }
    public bool HasConfiguredToken { get; }
    public string? Token { get; }
    public string TokenEnvironmentVariable { get; }
    public string TokenSource { get; }
    public int RecentAuditCapacity { get; }
}
