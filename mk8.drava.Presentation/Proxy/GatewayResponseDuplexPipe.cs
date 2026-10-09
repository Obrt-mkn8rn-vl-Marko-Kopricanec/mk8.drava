using System.IO.Pipelines;

namespace Mk8.Drava.Presentation.Proxy;

internal sealed record GatewayResponseDuplexPipe(PipeReader Input, PipeWriter Output) : IDuplexPipe;
