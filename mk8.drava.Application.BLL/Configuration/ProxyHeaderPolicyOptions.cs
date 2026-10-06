namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyHeaderPolicyOptions
{
    public System.Collections.ObjectModel.Collection<ProxyHeaderSetOptions> SetRequestHeaders { get; init; } = [];
    public System.Collections.ObjectModel.Collection<string> RemoveRequestHeaders { get; init; } = [];
    public System.Collections.ObjectModel.Collection<ProxyHeaderSetOptions> SetResponseHeaders { get; init; } = [];
    public System.Collections.ObjectModel.Collection<string> RemoveResponseHeaders { get; init; } = [];
}
