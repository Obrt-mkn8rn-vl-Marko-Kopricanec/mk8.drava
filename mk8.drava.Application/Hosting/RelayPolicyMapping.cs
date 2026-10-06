using Mk8.Drava.Application.BLL.NodeRelay;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Hosting;

internal static class RelayPolicyMapping
{
    public static RelayConnectionPolicy ToPolicy(RelayLimits limits) => new(limits.MaximumConcurrentConnections, limits.MaximumBytesPerDirection,
        limits.MaximumDurationSeconds, limits.StreamWindowFrames, limits.CapabilityLifetimeSeconds, limits.OpeningTimeoutSeconds);
}
