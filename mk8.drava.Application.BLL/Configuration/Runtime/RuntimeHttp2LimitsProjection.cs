namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHttp2LimitsProjection
{
    public RuntimeHttp2LimitsProjection(int MaxConcurrentStreams, int MaxHeaderListBytes, int MaxFrameSize)
    {
        RuntimeHttp2LimitFacts.Validate(MaxConcurrentStreams, MaxHeaderListBytes, MaxFrameSize);
        this.MaxConcurrentStreams = MaxConcurrentStreams;
        this.MaxHeaderListBytes = MaxHeaderListBytes;
        this.MaxFrameSize = MaxFrameSize;
    }

    public int MaxConcurrentStreams { get; }
    public int MaxHeaderListBytes { get; }
    public int MaxFrameSize { get; }
}
