using System.Buffers.Binary;
using System.Net;
using Haukcode.Mdns;

namespace Mk8.Drava.Transport.Discovery;

internal sealed class MdnsCandidateReader(string siteId, LocalDiscoveryNetwork network)
{
    internal const string ServiceType = "_mk8-drava._tcp.local.";
    private readonly Dictionary<string, DnsRecord> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<IPAddress>> _addresses = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DiscoveryCandidate> Read(byte[] packet, ushort queryId, bool responseFromLocalInterface = false)
    {
        if (packet.Length is < 12 or > 9000 || BinaryPrimitives.ReadUInt16BigEndian(packet) != queryId) return [];
        var records = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4)) + BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6)) +
            BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(8)) + BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(10));
        if (records > 64 || !DnsParser.TryParse(packet, out var parsed) || parsed is null || !parsed.IsResponse) return [];
        try
        {
            for (var index = 0; index < parsed.Answers.Count; index++) Add(parsed.Answers[index], packet);
            for (var index = 0; index < parsed.Additionals.Count; index++) Add(parsed.Additionals[index], packet);
            var candidates = new List<DiscoveryCandidate>();
            foreach (var entry in _services)
            {
                if (candidates.Count == 16) break;
                if (!_properties.TryGetValue(entry.Key, out var properties) || !properties.TryGetValue("site", out var site) ||
                    !string.Equals(site, siteId, StringComparison.Ordinal) || !properties.TryGetValue("v", out var version) || !string.Equals(version, "1", StringComparison.Ordinal)) continue;
                // Record data is retained only after removing DNS compression pointers using the package parser.
                var (_, _, port, target) = DnsParser.ParseSrv(entry.Value.Data, entry.Value.Data);
                if (!_addresses.TryGetValue(target, out var addresses) || port == 0) continue;
                for (var index = 0; index < addresses.Count; index++)
                {
                    var address = addresses[index];
                    if (candidates.Count == 16) break;
                    if (network.Contains(address) || (responseFromLocalInterface && IPAddress.IsLoopback(address))) candidates.Add(new DiscoveryCandidate(address.ToString(), port));
                }
            }
            return candidates;
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException or InvalidDataException) { return []; }
    }

    private void Add(DnsRecord record, byte[] packet)
    {
        if (record.Ttl == 0 || record.Name.Length > 253) return;
        if (record.Name.EndsWith("." + ServiceType, StringComparison.OrdinalIgnoreCase))
        {
            if (record.Type == DnsRecordType.SRV && (_services.Count < 64 || _services.ContainsKey(record.Name)))
            {
                var (priority, weight, port, target) = DnsParser.ParseSrv(record.Data, packet);
                if (target.Length <= 253) _services[record.Name] = record with { Data = DnsEncoder.BuildSrv(priority, weight, port, target) };
            }
            if (record.Type == DnsRecordType.TXT && record.Data.Length <= 1024 && (_properties.Count < 64 || _properties.ContainsKey(record.Name)))
                _properties[record.Name] = DnsParser.ParseTxt(record.Data);
        }
        else if (((record.Type == DnsRecordType.A && record.Data.Length == 4) || ((ushort)record.Type == 28 && record.Data.Length == 16)) && (_addresses.Count < 128 || _addresses.ContainsKey(record.Name)))
        {
            if (!_addresses.TryGetValue(record.Name, out var addresses)) _addresses.Add(record.Name, addresses = []);
            var address = new IPAddress(record.Data);
            if (addresses.Count < 16 && !addresses.Contains(address)) addresses.Add(address);
        }
    }
}
