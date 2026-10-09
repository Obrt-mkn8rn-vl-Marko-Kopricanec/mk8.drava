namespace Mk8.Drava.Application.INF.Dns.Management;

internal sealed record RrsetKey(ReadOnlyMemory<byte> Owner, ushort Type);
