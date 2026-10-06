namespace Mk8.Drava.Application.BLL.NodeRelay;

public sealed class RelayConnectionPolicy
{
    public int MaximumConcurrentConnections { get; }
    public long MaximumBytesPerDirection { get; }
    public int MaximumDurationSeconds { get; }
    public int StreamWindowFrames { get; }
    public int CapabilityLifetimeSeconds { get; }
    public int OpeningTimeoutSeconds { get; }

    public RelayConnectionPolicy(int maximumConcurrentConnections, long maximumBytesPerDirection, int maximumDurationSeconds,
        int streamWindowFrames, int capabilityLifetimeSeconds, int openingTimeoutSeconds)
    {
        if (maximumConcurrentConnections is < 1 or > 4096 || maximumBytesPerDirection is < 1 or > 4L * 1024 * 1024 * 1024 ||
            maximumDurationSeconds is < 1 or > 3600 || streamWindowFrames is < 1 or > 8 || capabilityLifetimeSeconds is < 1 or > 15 || openingTimeoutSeconds is < 1 or > 15)
            throw new InvalidDataException("Invalid relay resource policy.");
        MaximumConcurrentConnections = maximumConcurrentConnections; MaximumBytesPerDirection = maximumBytesPerDirection;
        MaximumDurationSeconds = maximumDurationSeconds; StreamWindowFrames = streamWindowFrames;
        CapabilityLifetimeSeconds = capabilityLifetimeSeconds; OpeningTimeoutSeconds = openingTimeoutSeconds;
    }
}
