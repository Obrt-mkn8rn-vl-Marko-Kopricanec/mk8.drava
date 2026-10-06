using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHeaderFieldProjection
{
    public RuntimeHeaderFieldProjection(string Name, string Value)
    {
        ProxyHeaderPolicyFacts.ValidateSetHeader(Name, Value);
        this.Name = Name;
        this.Value = Value;
    }

    public string Name { get; }
    public string Value { get; }
}
