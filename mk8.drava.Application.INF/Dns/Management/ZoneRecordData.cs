namespace Mk8.Drava.Application.INF.Dns.Management;

internal sealed record ZoneRecordData(ReadOnlyMemory<byte> Owner, ushort Type, uint Ttl, ReadOnlyMemory<byte> Data);
