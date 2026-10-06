using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public abstract record Http3AltSvcHeaderResult
{
    private Http3AltSvcHeaderResult()
    {
    }

    public static Http3AltSvcHeaderResult Suppressed { get; } = new SuppressedResult();

    public static Http3AltSvcHeaderResult Emitted(ProxyHeaderField header)
    {
        return new EmittedResult(header);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record SuppressedResult : Http3AltSvcHeaderResult;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record EmittedResult : Http3AltSvcHeaderResult
    {
        public EmittedResult(ProxyHeaderField header)
        {
            ArgumentNullException.ThrowIfNull(header);
            Header = header;
        }

        public ProxyHeaderField Header { get; }
    }
}
