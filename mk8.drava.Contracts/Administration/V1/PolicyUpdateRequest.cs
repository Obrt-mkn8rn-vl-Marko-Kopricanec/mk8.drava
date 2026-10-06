using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class PolicyUpdateRequest
{
    public long ExpectedRevision { get; init; }
    public bool ImportFile { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Policy { get; init; }
}
