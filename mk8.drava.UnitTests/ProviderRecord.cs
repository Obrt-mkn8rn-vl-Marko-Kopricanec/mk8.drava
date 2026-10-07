namespace Mk8.Drava.UnitTests;

internal sealed record ProviderRecord(string Name, string Type, string Content, bool Proxied = false, string Comment = "foreign owner")
{
    public string Id { get; init; } = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
}
