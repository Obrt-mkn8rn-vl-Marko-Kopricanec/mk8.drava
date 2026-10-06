using Grpc.Core;
using Mk8.Drava.Transport.Discovery;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Registration;

internal static class RegistrationGatewayFinder
{
    public static async ValueTask<SiteRegistrationChannel?> FindAsync(DravaRegistrationOptions options, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var seeds = options.GatewaySeeds.Count > 0 ? options.GatewaySeeds : [new DiscoveryCandidate("127.0.0.1", 9443)];
            var channel = await VerifyAsync(options, seeds, deadline.Token).ConfigureAwait(false);
            if (channel is not null || !options.MulticastDiscovery) return channel;
            using var discovery = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            discovery.CancelAfter(TimeSpan.FromSeconds(5));
            var candidates = await SiteDiscovery.FindAsync(options.Site.SiteId, discovery.Token).ConfigureAwait(false);
            return await VerifyAsync(options, candidates, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    private static async ValueTask<SiteRegistrationChannel?> VerifyAsync(DravaRegistrationOptions options, IReadOnlyList<DiscoveryCandidate> candidates, CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SiteRegistrationChannel? channel = null;
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(TimeSpan.FromSeconds(2));
                channel = new SiteRegistrationChannel(options.Site, candidate);
                await channel.VerifySiteAsync(attempt.Token).ConfigureAwait(false);
                var accepted = channel;
                channel = null;
                return accepted;
            }
            catch (Exception exception) when (exception is RpcException or HttpRequestException or InvalidDataException or System.Security.Cryptography.CryptographicException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            finally { channel?.Dispose(); }
        }
        return null;
    }
}
