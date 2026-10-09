namespace Mk8.Drava.Application.INF.Dns.Management;

internal sealed record RrsetChange(ReadOnlyMemory<byte> Owner, ushort Type, uint Ttl, IReadOnlyList<ReadOnlyMemory<byte>> Add, IReadOnlyList<ReadOnlyMemory<byte>> Remove, bool Replace);
