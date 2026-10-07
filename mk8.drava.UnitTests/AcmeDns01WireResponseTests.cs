using System.Buffers.Binary;
using System.Text;
using Mk8.Drava.Application.INF.Acme;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AcmeDns01WireResponseTests
{
    private const ushort Transaction = 0x1234;
    private const string Host = "_acme-challenge.site.example";
    private static readonly string Value = new('A', 43);

    [Fact]
    public void CharacterStringsCombineWithinOneRecordAndForeignValuesCoexist()
    {
        var packet = Response(["foreign value"], [Value[..20], Value[20..]]);
        Assert.True(AcmeDns01WireResponse.HasProof(packet, Transaction, Host, Value));
        Assert.False(AcmeDns01WireResponse.HasProof(packet, Transaction, Host, new string('B', 43)));
    }

    [Fact]
    public void SeparateTxtRecordsCannotBeConcatenatedIntoOneDigest()
    {
        Assert.False(AcmeDns01WireResponse.HasProof(Response([Value[..20]], [Value[20..]]), Transaction, Host, Value));
    }

    [Theory]
    [InlineData("transaction")]
    [InlineData("query-header")]
    [InlineData("questions")]
    [InlineData("question-type")]
    [InlineData("record-bound")]
    [InlineData("self-pointer")]
    [InlineData("forward-pointer")]
    [InlineData("reserved-label")]
    [InlineData("short-rdata")]
    [InlineData("short-string")]
    [InlineData("trailing")]
    public void MalformedDnsFramesCannotProvePublication(string corruption)
    {
        var packet = Response([Value]);
        var questionEnd = Question().Length;
        switch (corruption)
        {
            case "transaction": packet[0] ^= 1; break;
            case "query-header": packet[2] &= 0x7f; break;
            case "questions": packet[5] = 2; break;
            case "question-type": packet[questionEnd - 3] = 1; break;
            case "record-bound": packet[7] = 129; break;
            case "self-pointer": BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(questionEnd), checked((ushort)(0xc000 | questionEnd))); break;
            case "forward-pointer": BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(questionEnd), checked((ushort)(0xc000 | questionEnd + 2))); break;
            case "reserved-label": packet[questionEnd] = 0x40; break;
            case "short-rdata": BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(questionEnd + 10), 65535); break;
            case "short-string": packet[questionEnd + 12] = 255; break;
            case "trailing": packet = [.. packet, 0]; break;
            default: throw new InvalidOperationException();
        }
        Assert.Throws<InvalidDataException>(() => AcmeDns01WireResponse.HasProof(packet, Transaction, Host, Value));
    }

    [Fact]
    public void WrongOwnerAliasesAndErrorResponsesNeverCountAsProof()
    {
        var packet = Response([Value]);
        Assert.Throws<InvalidDataException>(() => AcmeDns01WireResponse.HasProof(packet, Transaction, "_acme-challenge.foreign.example", Value));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(Question().Length + 2), 5);
        Assert.False(AcmeDns01WireResponse.HasProof(packet, Transaction, Host, Value));
        packet = Response([Value]); packet[3] = 0x83;
        Assert.False(AcmeDns01WireResponse.HasProof(packet, Transaction, Host, Value));
    }

    [Fact]
    public void TruncationSelectsTcpButCannotItselfProveTheDigest()
    {
        var packet = Response([Value]); packet[2] |= 2;
        Assert.True(AcmeDns01WireResponse.IsTruncated(packet, Transaction));
        Assert.False(AcmeDns01WireResponse.HasProof(packet, Transaction, Host, Value));
    }

    private static byte[] Question()
    {
        var bytes = new List<byte> { 0x12, 0x34, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in Host.Split('.')) { bytes.Add(checked((byte)label.Length)); bytes.AddRange(Encoding.ASCII.GetBytes(label)); }
        bytes.AddRange([0, 0, 16, 0, 1]); return bytes.ToArray();
    }

    private static byte[] Response(params string[][] records)
    {
        var bytes = new List<byte>(Question()); bytes[2] = 0x81; bytes[3] = 0x80;
        bytes[6] = checked((byte)(records.Length >> 8)); bytes[7] = checked((byte)records.Length);
        foreach (var record in records)
        {
            var length = record.Sum(static part => part.Length + 1);
            bytes.AddRange([0xc0, 12, 0, 16, 0, 1, 0, 0, 0, 60, checked((byte)(length >> 8)), checked((byte)length)]);
            foreach (var part in record) { bytes.Add(checked((byte)part.Length)); bytes.AddRange(Encoding.ASCII.GetBytes(part)); }
        }
        return bytes.ToArray();
    }
}
