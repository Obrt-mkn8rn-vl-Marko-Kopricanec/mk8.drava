using System.Net;
using DnsClient;
using DnsClient.Protocol;

namespace Mk8.Drava.Application.INF.Acme;

internal sealed class AcmeDns01PropagationVerifier : IAcmeDns01PropagationVerifier
{
    private readonly LookupClient _client;
    public AcmeDns01PropagationVerifier(string resolverAddress, int resolverPort)
    {
        ArgumentNullException.ThrowIfNull(resolverAddress);
        if (resolverPort is < 1 or > 65535 || resolverAddress.Length > 0 && !IPAddress.TryParse(resolverAddress, out _))
            throw new InvalidDataException("ACME propagation requires an approved DNS resolver literal and port.");
        var options = resolverAddress.Length == 0 ? new LookupClientOptions() : new LookupClientOptions(new IPEndPoint(IPAddress.Parse(resolverAddress), resolverPort));
        options.UseCache = false; options.Retries = 0; options.Timeout = TimeSpan.FromSeconds(2);
        options.ExtendedDnsBufferSize = 1232; options.UseTcpFallback = true; options.ThrowDnsErrors = false;
        _client = new LookupClient(options);
    }

    public async ValueTask<bool> VerifyAsync(string host, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host); ArgumentException.ThrowIfNullOrWhiteSpace(value);
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var response = await _client.QueryAsync(host, QueryType.TXT, QueryClass.IN, deadline.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (response.HasError || response.Answers.Count > 128) return false;
            var expected = host + ".";
            var found = false;
            foreach (var record in response.Answers)
            {
                if (record is not TxtRecord txt || !string.Equals(txt.DomainName.Value, expected, StringComparison.OrdinalIgnoreCase)) return false;
                var length = 0;
                foreach (var text in txt.Text)
                {
                    if (text.Length > 4096 - length) return false;
                    length += text.Length;
                }
                if (length == value.Length && string.Equals(string.Concat(txt.Text), value, StringComparison.Ordinal)) found = true;
            }
            return found;
        }
        catch (DnsResponseException) { cancellationToken.ThrowIfCancellationRequested(); return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
}
