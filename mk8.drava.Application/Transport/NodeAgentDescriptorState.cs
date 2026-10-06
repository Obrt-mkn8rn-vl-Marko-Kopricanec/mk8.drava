using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Transport;

internal sealed class NodeAgentDescriptorState
{
    private NodeAgentDescriptor? _descriptor;
    public NodeAgentDescriptor? Read() => Volatile.Read(ref _descriptor);
    public void Install(NodeAgentDescriptor descriptor) { descriptor.Validate(); Volatile.Write(ref _descriptor, descriptor); }
}
