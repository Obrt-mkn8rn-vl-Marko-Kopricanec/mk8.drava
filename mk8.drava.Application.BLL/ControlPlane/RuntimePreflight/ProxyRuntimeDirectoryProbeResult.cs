namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
public abstract record ProxyRuntimeDirectoryProbeResult
{
    private ProxyRuntimeDirectoryProbeResult()
    {
    }

    public abstract bool Exists { get; }
    public abstract bool Created { get; }
    public abstract bool CanRead { get; }
    public abstract bool CanWrite { get; }

    public static ProxyRuntimeDirectoryProbeResult Missing()
    {
        return MissingResult.Instance;
    }

    public static ProxyRuntimeDirectoryProbeResult Probed(bool created, bool canRead, bool canWrite)
    {
        return new ProbedResult(created, canRead, canWrite);
    }

    public static ProxyRuntimeDirectoryProbeResult AccessDenied(bool exists, bool created)
    {
        return new AccessDeniedResult(exists, created);
    }

    public static ProxyRuntimeDirectoryProbeResult IoError(bool exists, bool created)
    {
        return new IoErrorResult(exists, created);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingResult : ProxyRuntimeDirectoryProbeResult
    {
        public static MissingResult Instance { get; } = new();

        private MissingResult()
        {
        }

        public override bool Exists => false;
        public override bool Created => false;
        public override bool CanRead => false;
        public override bool CanWrite => false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record ProbedResult : ProxyRuntimeDirectoryProbeResult
    {
        public ProbedResult(bool created, bool canRead, bool canWrite)
        {
            Created = created;
            CanRead = canRead;
            CanWrite = canWrite;
        }

        public override bool Exists => true;
        public override bool Created { get; }
        public override bool CanRead { get; }
        public override bool CanWrite { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AccessDeniedResult : ProxyRuntimeDirectoryProbeResult
    {
        public AccessDeniedResult(bool exists, bool created)
        {
            Exists = exists;
            Created = created;
        }

        public override bool Exists { get; }
        public override bool Created { get; }
        public override bool CanRead => false;
        public override bool CanWrite => false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record IoErrorResult : ProxyRuntimeDirectoryProbeResult
    {
        public IoErrorResult(bool exists, bool created)
        {
            Exists = exists;
            Created = created;
        }

        public override bool Exists { get; }
        public override bool Created { get; }
        public override bool CanRead => false;
        public override bool CanWrite => false;
    }
}
