using System.Text.Json.Serialization;

namespace Mk8.Drava.Application.INF.Dns.Management;

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, MaxDepth = 32)]
[JsonSerializable(typeof(ManagementApiRequest))]
[JsonSerializable(typeof(ManagementReply))]
[JsonSerializable(typeof(ManagementHttpError))]
internal sealed partial class ManagementHttpJsonContext : JsonSerializerContext;
