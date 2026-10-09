using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Proxy;

internal interface IGatewayInformationalResponseFeature
{
    Task WriteAsync(ResponseHead head, int streamId, CancellationToken cancellationToken);
}
