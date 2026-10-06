namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record AdminBindResolution
{
    public AdminBindResolution(IReadOnlyList<string> Urls, string Source, bool ApplyToWebHost, bool IsLocalOnly, bool RequireAuthentication, bool HasConfiguredToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        this.Urls = RuntimeList.Copy(Urls);
        if (this.Urls.Count == 0)
        {
            throw new ArgumentException("Admin bind resolution must contain at least one URL.", nameof(Urls));
        }

        this.Source = Source;
        this.ApplyToWebHost = ApplyToWebHost;
        this.IsLocalOnly = IsLocalOnly;
        this.RequireAuthentication = RequireAuthentication;
        this.HasConfiguredToken = HasConfiguredToken;
    }

    public IReadOnlyList<string> Urls { get; }
    public string Source { get; init; }
    public bool ApplyToWebHost { get; init; }
    public bool IsLocalOnly { get; init; }
    public bool RequireAuthentication { get; init; }
    public bool HasConfiguredToken { get; init; }
}
