namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeHttp2LimitsResponse(int MaxConcurrentStreams, int MaxHeaderListBytes, int MaxFrameSize)
{
}
