namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyReadinessStatusResponse
{
    public ProxyReadinessStatusResponse(string state, IReadOnlyList<string> reasons, DateTimeOffset generatedAtUtc, int? configGeneration)
    {
        State = state;
        Reasons = ApiResponseList.Copy(reasons);
        GeneratedAtUtc = generatedAtUtc;
        ConfigGeneration = configGeneration;
    }

    public string State { get; }
    public IReadOnlyList<string> Reasons { get; }
    public DateTimeOffset GeneratedAtUtc { get; }
    public int? ConfigGeneration { get; }
}
