namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeHeaderPolicyResponse
{
    public RuntimeHeaderPolicyResponse(IReadOnlyList<RuntimeHeaderFieldResponse> setRequestHeaders, IReadOnlyList<string> removeRequestHeaders, IReadOnlyList<RuntimeHeaderFieldResponse> setResponseHeaders, IReadOnlyList<string> removeResponseHeaders)
    {
        SetRequestHeaders = ApiResponseList.Copy(setRequestHeaders);
        RemoveRequestHeaders = ApiResponseList.Copy(removeRequestHeaders);
        SetResponseHeaders = ApiResponseList.Copy(setResponseHeaders);
        RemoveResponseHeaders = ApiResponseList.Copy(removeResponseHeaders);
    }

    public IReadOnlyList<RuntimeHeaderFieldResponse> SetRequestHeaders { get; }
    public IReadOnlyList<string> RemoveRequestHeaders { get; }
    public IReadOnlyList<RuntimeHeaderFieldResponse> SetResponseHeaders { get; }
    public IReadOnlyList<string> RemoveResponseHeaders { get; }
}
