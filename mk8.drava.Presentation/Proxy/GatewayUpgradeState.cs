using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Proxy;

// The exchange owner joins both directions before disposing the shared public stream.
internal sealed class GatewayUpgradeState(bool requested) : IAsyncDisposable
{
    private readonly TaskCompletionSource<Stream>? _accepted = requested ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
    internal static GatewayUpgradeState None { get; } = new(requested: false);
    internal bool Requested { get; } = requested;
    internal Stream? AcceptedStream { get; private set; }

    internal void Validate(HttpContext context, ResponseHead head)
    {
        if (!Requested || AcceptedStream is not null || !string.Equals(context.Request.Protocol, "HTTP/1.1", StringComparison.Ordinal) ||
            context.Features.Get<IHttpUpgradeFeature>() is not { IsUpgradableRequest: true })
            throw new InvalidDataException("Public transport did not negotiate an upgrade.");
        string? protocol = null;
        foreach (var field in head.Headers)
        {
            if (field.Name.Equals("upgrade", StringComparison.OrdinalIgnoreCase))
            {
                if (protocol is not null) throw new InvalidDataException("Duplicate upgrade protocol.");
                protocol = field.Value;
            }
            if (field.Name.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Equals("trailer", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An upgrade cannot carry HTTP body framing.");
        }
        if (protocol is null || !string.Equals(protocol, context.Request.Headers.Upgrade.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The accepted upgrade protocol does not match the request.");
    }

    internal async ValueTask AcceptAsync(HttpContext context)
    {
        if (!Requested || AcceptedStream is not null) throw new InvalidDataException("Upgrade stream cannot be accepted in this state.");
        var accepted = _accepted ?? throw new InvalidDataException("Upgrade acceptance is unavailable.");
        var feature = context.Features.Get<IHttpUpgradeFeature>() ?? throw new InvalidDataException("Public upgrade feature is missing.");
        AcceptedStream = await feature.UpgradeAsync().ConfigureAwait(false);
        accepted.TrySetResult(AcceptedStream);
    }

    internal async ValueTask<Stream> WaitForStreamAsync(CancellationToken cancellationToken) =>
        await (_accepted ?? throw new InvalidDataException("An upgrade was not requested.")).Task.WaitAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (AcceptedStream is { } stream) await stream.DisposeAsync().ConfigureAwait(false);
    }
}
