using System.Buffers.Binary;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.IntegrationTests;
internal enum Http2TestFrameType : byte
{
    Data = 0x0,
    Headers = 0x1,
    RstStream = 0x3,
    Settings = 0x4,
    Ping = 0x6,
    GoAway = 0x7,
    WindowUpdate = 0x8,
    Continuation = 0x9
}
