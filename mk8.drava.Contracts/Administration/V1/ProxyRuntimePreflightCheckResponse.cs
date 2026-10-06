namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyRuntimePreflightCheckResponse(string Name, string RelativePath, bool Exists, bool Created, bool CanRead, bool CanWrite, string Severity, string Reason)
{
}
