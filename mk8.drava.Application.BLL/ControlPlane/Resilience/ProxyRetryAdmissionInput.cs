using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public sealed record ProxyRetryAdmissionInput
{
    public ProxyRetryAdmissionInput(bool Enabled, int MaxAttempts, IReadOnlyList<string> RetryMethods, string RequestMethod, bool HasRequestBody)
    {
        ArgumentNullException.ThrowIfNull(RetryMethods);
        ArgumentNullException.ThrowIfNull(RequestMethod);
        this.Enabled = Enabled;
        this.MaxAttempts = MaxAttempts;
        this.RetryMethods = CopyRetryMethods(RetryMethods);
        this.RequestMethod = RequestMethod;
        this.HasRequestBody = HasRequestBody;
    }

    public bool Enabled { get; }
    public int MaxAttempts { get; }
    public IReadOnlyList<string> RetryMethods { get; }
    public string RequestMethod { get; }
    public bool HasRequestBody { get; }

    private static IReadOnlyList<string> CopyRetryMethods(IReadOnlyList<string> methods)
    {
        var copy = new List<string>();
        foreach (var method in methods)
        {
            ArgumentNullException.ThrowIfNull(method);
            copy.Add(method);
        }

        return new ReadOnlyCollection<string>(copy);
    }
}
