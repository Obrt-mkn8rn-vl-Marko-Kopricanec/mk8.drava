namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeCanonicalHostResponse(bool Enabled, string TargetHost, int StatusCode)
{
}
