namespace Mk8.Drava.Configuration;

public sealed record GatewayPlanSettings
{
    public int RefreshSeconds { get; init; } = 5;
    public int RetrySeconds { get; init; } = 1;
    public int RequestDeadlineSeconds { get; init; } = 3;
    public int MaximumRetainedGenerations { get; init; } = 16;
    public int TlsHandshakeSeconds { get; init; } = 5;

    public void Validate()
    {
        if (RefreshSeconds is < 1 or > 60 || RetrySeconds is < 1 or > 30 || RequestDeadlineSeconds is < 1 or > 15 ||
            MaximumRetainedGenerations is < 2 or > 64 || TlsHandshakeSeconds is < 1 or > 30)
            throw new InvalidDataException("Invalid Gateway plan refresh or material settings.");
    }
}
