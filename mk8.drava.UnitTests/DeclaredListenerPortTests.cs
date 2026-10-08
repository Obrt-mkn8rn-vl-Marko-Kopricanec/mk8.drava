using System.Globalization;
using Mk8.Drava.CompatibilityTests;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class DeclaredListenerPortTests
{
    [LinuxPortTheory]
    [InlineData("tcp")]
    [InlineData("udp")]
    [InlineData("tcp/udp")]
    public void ListenerPortsRemainOutsideTheKernelAutomaticClientPortRange(string protocol)
    {
        var values = File.ReadAllText("/proc/sys/net/ipv4/ip_local_port_range").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, values.Length);
        var first = int.Parse(values[0], NumberStyles.None, CultureInfo.InvariantCulture);
        var last = int.Parse(values[1], NumberStyles.None, CultureInfo.InvariantCulture);
        var issued = new HashSet<int>();
        for (var index = 0; index < 16; index++)
        {
            var port = protocol switch
            {
                "tcp" => TestPortAllocator.GetFreeTcpPort(),
                "udp" => TestPortAllocator.GetFreeUdpPort(),
                "tcp/udp" => TestPortAllocator.GetFreeTcpUdpPort(),
                _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
            };
            Assert.True(port < first || port > last, $"Declared {protocol} listener port {port} overlaps automatic client ports {first}..{last}.");
            Assert.True(issued.Add(port), $"Declared listener port {port} was reused in the same run.");
        }
    }
}
