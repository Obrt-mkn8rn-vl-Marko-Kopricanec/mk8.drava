namespace Mk8.Drava.Configuration;

public sealed record RelayLimits
{
    public int MaximumConcurrentConnections { get; init; } = 256;
    public long MaximumBytesPerDirection { get; init; } = 512L * 1024 * 1024;
    public int MaximumDurationSeconds { get; init; } = 600;
    public int StreamWindowFrames { get; init; } = 4;
    public int CapabilityLifetimeSeconds { get; init; } = 10;
    public int OpeningTimeoutSeconds { get; init; } = 3;

    public void Validate()
    {
        if (MaximumConcurrentConnections is < 1 or > 4096 || MaximumBytesPerDirection is < 1 or > 4L * 1024 * 1024 * 1024 ||
            MaximumDurationSeconds is < 1 or > 3600 || StreamWindowFrames is < 1 or > 8 || CapabilityLifetimeSeconds is < 1 or > 15 || OpeningTimeoutSeconds is < 1 or > 15)
            throw new InvalidDataException("Invalid relay resource limits.");
    }
}
