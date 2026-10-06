namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record AdminBindCandidate
{
    public AdminBindCandidate(IReadOnlyList<string> Urls, string Source, bool ApplyToWebHost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        this.Urls = RuntimeList.Copy(Urls);
        this.Source = Source;
        this.ApplyToWebHost = ApplyToWebHost;
    }

    public IReadOnlyList<string> Urls { get; }
    public string Source { get; init; }
    public bool ApplyToWebHost { get; init; }
}
