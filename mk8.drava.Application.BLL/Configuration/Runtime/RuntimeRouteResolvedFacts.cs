namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeRouteResolvedFacts
{
    private static readonly TimeSpan MinimumTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(10);
    public static void Validate(long maxRequestBodyBytes, TimeSpan clientRequestHeadTimeout, TimeSpan upstreamResponseHeadTimeout)
    {
        if (maxRequestBodyBytes is < 0 or > 1L * 1024 * 1024 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequestBodyBytes));
        }

        ValidateTimeout(clientRequestHeadTimeout, nameof(clientRequestHeadTimeout));
        ValidateTimeout(upstreamResponseHeadTimeout, nameof(upstreamResponseHeadTimeout));
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout < MinimumTimeout || timeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
