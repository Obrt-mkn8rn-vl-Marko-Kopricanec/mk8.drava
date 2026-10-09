namespace Mk8.Drava.Application.INF.Dns.Management;

internal static class ProtocolVersion
{
    public const uint Current = 1;
    public const uint Management = 3;
    public const int MaximumZoneFileBytes = 1_000_000;
    public const int MaximumMessageBytes = 4096;
}
