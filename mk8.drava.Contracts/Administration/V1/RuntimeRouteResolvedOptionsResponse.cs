namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeRouteResolvedOptionsResponse(long MaxRequestBodyBytes, TimeSpan ClientRequestHeadTimeout, TimeSpan UpstreamResponseHeadTimeout, bool AccessLogEnabled)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeRouteTimeoutOverridesResponse? FlowTimeouts { get; init; }
}
