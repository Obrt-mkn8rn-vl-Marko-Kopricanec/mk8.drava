using System.Buffers.Binary;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class Http2TestFlags
{
    public const byte EndStream = 0x1;
    public const byte Ack = 0x1;
    public const byte EndHeaders = 0x4;
}
