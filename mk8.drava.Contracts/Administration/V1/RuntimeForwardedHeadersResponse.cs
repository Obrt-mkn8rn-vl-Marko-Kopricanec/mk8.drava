namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeForwardedHeadersResponse
{
    public RuntimeForwardedHeadersResponse(bool enabled, IReadOnlyList<string> trustedProxies)
    {
        Enabled = enabled;
        TrustedProxies = ApiResponseList.Copy(trustedProxies);
    }

    public bool Enabled { get; }
    public IReadOnlyList<string> TrustedProxies { get; }
}
