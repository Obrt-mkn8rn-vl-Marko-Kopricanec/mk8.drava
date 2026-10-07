namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record AdminStartupSecurityOptions
{
    public AdminStartupSecurityOptions(IReadOnlyList<string> Urls, bool RequireAuthentication, bool HasConfiguredToken)
    {
        this.Urls = CopyUrls(Urls);
        this.RequireAuthentication = RequireAuthentication;
        this.HasConfiguredToken = HasConfiguredToken;
    }

    public IReadOnlyList<string> Urls { get; }
    public bool RequireAuthentication { get; init; }
    public bool HasConfiguredToken { get; init; }
    public bool AuthenticationEnabled => RequireAuthentication && HasConfiguredToken;

    private static System.Collections.ObjectModel.ReadOnlyCollection<string> CopyUrls(IReadOnlyList<string> urls)
    {
        var copy = RuntimeList.Copy(urls);
        foreach (var url in copy)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Admin startup URL entries cannot be empty.", nameof(urls));
            }
        }

        return copy;
    }
}
