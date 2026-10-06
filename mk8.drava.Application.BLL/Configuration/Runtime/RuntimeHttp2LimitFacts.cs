namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeHttp2LimitFacts
{
    public static void Validate(int maxConcurrentStreams, int maxHeaderListBytes, int maxFrameSize)
    {
        if (maxConcurrentStreams is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentStreams));
        }

        if (maxHeaderListBytes is < 1024 or > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHeaderListBytes));
        }

        if (maxFrameSize is < 16 * 1024 or > 16 * 1024 * 1024 - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameSize));
        }
    }
}
