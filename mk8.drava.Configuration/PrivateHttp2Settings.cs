namespace Mk8.Drava.Configuration;

// Transport receive capacity is distinct from the application frame-credit window.
public sealed record PrivateHttp2Settings
{
    public int InitialStreamWindowBytes { get; init; } = 64 * 1024;
    public int InitialConnectionWindowBytes { get; init; } = 256 * 1024;
    public int MaximumStreamsPerConnection { get; init; } = 128;

    public void Validate()
    {
        // Keep explicit resource ceilings even though HTTP/2 permits larger windows.
        if (InitialStreamWindowBytes is < 65535 or > 16 * 1024 * 1024 ||
            InitialConnectionWindowBytes is < 65535 or > 64 * 1024 * 1024 ||
            MaximumStreamsPerConnection is < 1 or > 1024)
        {
            throw new InvalidDataException("Invalid private HTTP/2 receive limits.");
        }
    }
}
