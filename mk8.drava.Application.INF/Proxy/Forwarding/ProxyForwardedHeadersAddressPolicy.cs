using System.Net;
using System.Net.Sockets;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
public sealed class ProxyForwardedHeadersAddressPolicy : IProxyTrustedProxyPolicy, IForwardedHeadersAddressPolicy
{
    public bool IsValidEntry(string entry)
    {
        return TryParseTrustedProxy(entry, out _);
    }

    public bool IsTrustedPeer(string peerAddress, IReadOnlyList<string> trustedProxyEntries)
    {
        if (!TryParseAddress(peerAddress, out var parsedPeer))
        {
            return false;
        }

        foreach (var entry in trustedProxyEntries)
        {
            if (TryParseTrustedProxy(entry, out var trustedProxy) && trustedProxy.Contains(parsedPeer))
            {
                return true;
            }
        }

        return false;
    }

    public ForwardedForNormalizationResult NormalizeForwardedFor(IReadOnlyList<string> forwardedFor)
    {
        foreach (var entry in forwardedFor)
        {
            var value = entry.Trim().Trim('"');
            if (value.Length == 0)
            {
                continue;
            }

            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                value = value[1..^1];
            }

            if (TryParseAddress(value, out var parsed))
            {
                return ForwardedForNormalizationResult.Normalized(parsed.ToString());
            }
        }

        return ForwardedForNormalizationResult.Missing;
    }

    private static bool TryParseTrustedProxy(string entry, out TrustedProxyEntry trustedProxy)
    {
        trustedProxy = default;
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        var trimmed = entry.Trim();
        var slashIndex = trimmed.IndexOf('/');
        var addressText = slashIndex < 0 ? trimmed : trimmed[..slashIndex];
        if (!TryParseAddress(addressText, out var address))
        {
            return false;
        }

        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefixLength = maxPrefix;
        if (slashIndex >= 0)
        {
            var prefixText = trimmed[(slashIndex + 1)..];
            if (!int.TryParse(prefixText, out prefixLength) || prefixLength < 0 || prefixLength > maxPrefix)
            {
                return false;
            }
        }

        trustedProxy = new TrustedProxyEntry(trimmed, address, prefixLength);
        return true;
    }

    private static bool TryParseAddress(string value, out IPAddress address)
    {
        if (!IPAddress.TryParse(value, out var parsed))
        {
            address = IPAddress.None;
            return false;
        }

        address = NormalizeAddress(parsed);
        return true;
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private readonly record struct TrustedProxyEntry(string Entry, IPAddress NetworkAddress, int PrefixLength)
    {
        public bool Contains(IPAddress address)
        {
            var normalizedAddress = NormalizeAddress(address);
            var normalizedNetwork = NormalizeAddress(NetworkAddress);
            if (normalizedAddress.AddressFamily != normalizedNetwork.AddressFamily)
            {
                return false;
            }

            var addressBytes = normalizedAddress.GetAddressBytes();
            var networkBytes = normalizedNetwork.GetAddressBytes();
            var remainingBits = PrefixLength;
            for (var index = 0; index < addressBytes.Length; index++)
            {
                if (remainingBits <= 0)
                {
                    return true;
                }

                var bitsInByte = Math.Min(8, remainingBits);
                var mask = (byte)(0xff << (8 - bitsInByte));
                if ((addressBytes[index] & mask) != (networkBytes[index] & mask))
                {
                    return false;
                }

                remainingBits -= bitsInByte;
            }

            return true;
        }
    }
}
