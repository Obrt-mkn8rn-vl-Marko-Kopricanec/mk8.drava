namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyConfigurationNormalizeResponse
{
    public ProxyConfigurationNormalizeResponse(bool succeeded, string format, string? canonicalJson, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileErrorResponse> fileErrors)
    {
        Succeeded = succeeded;
        Format = format;
        CanonicalJson = canonicalJson;
        Errors = ApiResponseList.Copy(errors);
        FileErrors = ApiResponseList.Copy(fileErrors);
    }

    public bool Succeeded { get; }
    public string Format { get; }
    public string? CanonicalJson { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<ProxyConfigurationFileErrorResponse> FileErrors { get; }
}
