namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyRuntimePreflightStatusResponse
{
    public ProxyRuntimePreflightStatusResponse(string state, DateTimeOffset? generatedAtUtc, IReadOnlyList<string> reasons, IReadOnlyList<ProxyRuntimePreflightCheckResponse> checks)
    {
        State = state;
        GeneratedAtUtc = generatedAtUtc;
        Reasons = ApiResponseList.Copy(reasons);
        Checks = ApiResponseList.Copy(checks);
    }

    public string State { get; }
    public DateTimeOffset? GeneratedAtUtc { get; }
    public IReadOnlyList<string> Reasons { get; }
    public IReadOnlyList<ProxyRuntimePreflightCheckResponse> Checks { get; }
}
