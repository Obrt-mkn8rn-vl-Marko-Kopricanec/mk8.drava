using System.Buffers.Binary;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.UnitTests;
internal readonly record struct Http2TestFrame(Http2TestFrameType Type, byte Flags, int StreamId, ReadOnlyMemory<byte> Payload);
