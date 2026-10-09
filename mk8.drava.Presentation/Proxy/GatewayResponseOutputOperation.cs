namespace Mk8.Drava.Presentation.Proxy;

internal sealed record GatewayResponseOutputOperation(byte[] Bytes, long Fence, CancellationToken Token)
{
    public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
