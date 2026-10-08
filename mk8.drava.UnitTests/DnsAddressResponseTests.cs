using System.Buffers.Binary;
using System.Net;
using System.Text;
using Mk8.Drava.Application.INF.Dns;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class DnsAddressResponseTests
{
    private const ushort Transaction = 0x4321;
    private const string Host = "svc.site.example";

    [Theory]
    [InlineData(1, "192.0.2.10")]
    [InlineData(28, "2001:db8::10")]
    public void AddressRecordsRetainExactOwnerAddressAndUnsignedTtl(int type, string address)
    {
        var queryType = checked((ushort)type); var expected = IPAddress.Parse(address);
        var parsed = DnsAddressResponse.Read(Response(queryType, Record(Host, queryType, expected.GetAddressBytes(), 23)), Transaction, Host, queryType);
        Assert.True(parsed.Valid); Assert.Collection(parsed.Answers, answer => { Assert.Equal(Host, answer.Owner); Assert.Equal(expected, answer.Address); Assert.Equal(23u, answer.Ttl); Assert.Null(answer.Alias); });
    }

    [Fact]
    public void AliasAndItsAddressRemainSeparateScopedFacts()
    {
        var packet = Response(1, Record(Host, 5, Name("gateway.site.example")), Record("gateway.site.example", 1, IPAddress.Loopback.GetAddressBytes()));
        var parsed = DnsAddressResponse.Read(packet, Transaction, Host, 1); Assert.True(parsed.Valid);
        Assert.Collection(parsed.Answers, static alias => { Assert.Equal("gateway.site.example", alias.Alias); Assert.Null(alias.Address); },
            static address => { Assert.Equal("gateway.site.example", address.Owner); Assert.Equal(IPAddress.Loopback, address.Address); });
    }

    [Theory]
    [InlineData("transaction")]
    [InlineData("query-header")]
    [InlineData("questions")]
    [InlineData("question-type")]
    [InlineData("record-bound")]
    [InlineData("self-pointer")]
    [InlineData("forward-pointer")]
    [InlineData("short-rdata")]
    [InlineData("address-length")]
    [InlineData("wrong-class")]
    [InlineData("trailing")]
    public void MalformedAddressWireDataCannotSupplyPublicationFacts(string corruption)
    {
        var packet = Response(1, Record(Host, 1, IPAddress.Loopback.GetAddressBytes())); var start = Question(1).Length; var ownerLength = Name(Host).Length;
        switch (corruption)
        {
            case "transaction": packet[0] ^= 1; break;
            case "query-header": packet[2] &= 0x7f; break;
            case "questions": packet[5] = 2; break;
            case "question-type": packet[start - 3] = 28; break;
            case "record-bound": packet[7] = 65; break;
            case "self-pointer": BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(start), checked((ushort)(0xc000 | start))); break;
            case "forward-pointer": BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(start), checked((ushort)(0xc000 | start + 2))); break;
            case "short-rdata": BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(start + ownerLength + 8), 65535); break;
            case "address-length": BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(start + ownerLength + 8), 3); break;
            case "wrong-class": packet[start + ownerLength + 3] = 3; break;
            case "trailing": packet = [.. packet, 0]; break;
            default: throw new InvalidOperationException("Unknown owned wire corruption.");
        }
        Assert.Throws<InvalidDataException>(() => DnsAddressResponse.Read(packet, Transaction, Host, 1));
    }

    [Theory]
    [InlineData(0x8183)]
    [InlineData(0x8380)]
    public void ErrorAndStillTruncatedResponsesDoNotSupplyProof(int flags)
    {
        var packet = Response(1, Record(Host, 1, IPAddress.Loopback.GetAddressBytes()));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), checked((ushort)flags));
        Assert.False(DnsAddressResponse.Read(packet, Transaction, Host, 1).Valid);
    }

    [Fact]
    public void AnotherQuestionOwnerIsRejected()
    {
        var packet = Response(1, Record(Host, 1, IPAddress.Loopback.GetAddressBytes()));
        Assert.Throws<InvalidDataException>(() => DnsAddressResponse.Read(packet, Transaction, "foreign.site.example", 1));
    }

    [Fact]
    public void AliasMustConsumeItsExactDeclaredData()
    {
        var packet = Response(1, Record(Host, 5, [.. Name("gateway.site.example"), 0]));
        Assert.Throws<InvalidDataException>(() => DnsAddressResponse.Read(packet, Transaction, Host, 1));
    }

    private static byte[] Name(string value)
    {
        var bytes = new List<byte>();
        foreach (var label in value.Split('.')) { bytes.Add(checked((byte)label.Length)); bytes.AddRange(Encoding.ASCII.GetBytes(label)); }
        bytes.Add(0); return bytes.ToArray();
    }
    private static byte[] Question(ushort type) => [0x43, 0x21, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, .. Name(Host), (byte)(type >> 8), (byte)type, 0, 1];
    private static byte[] Record(string owner, ushort type, byte[] data, uint ttl = 60) => [.. Name(owner), (byte)(type >> 8), (byte)type, 0, 1,
        (byte)(ttl >> 24), (byte)(ttl >> 16), (byte)(ttl >> 8), (byte)ttl, (byte)(data.Length >> 8), (byte)data.Length, .. data];
    private static byte[] Response(ushort type, params byte[][] records)
    {
        var bytes = new List<byte>(Question(type)); bytes[2] = 0x81; bytes[3] = 0x80;
        bytes[7] = checked((byte)records.Length);
        foreach (var record in records) bytes.AddRange(record);
        return bytes.ToArray();
    }
}
