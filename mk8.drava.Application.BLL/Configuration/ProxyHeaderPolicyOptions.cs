namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyHeaderPolicyOptions
{
    public IList<ProxyHeaderSetOptions> SetRequestHeaders { get; init; } = [];
    public IList<string> RemoveRequestHeaders { get; init; } = [];
    public IList<ProxyHeaderSetOptions> SetResponseHeaders { get; init; } = [];
    public IList<string> RemoveResponseHeaders { get; init; } = [];
}
