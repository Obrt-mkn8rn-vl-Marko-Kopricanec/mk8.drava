using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public abstract partial record ProxyConfigLintSubmittedConfigurationResult
{
    private ProxyConfigLintSubmittedConfigurationResult()
    {
    }

    public static ProxyConfigLintSubmittedConfigurationResult Loaded(ProxyConfigLintConfigurationSnapshot snapshot, IReadOnlyList<ProxyConfigurationFileError> validationErrors)
    {
        return new LoadedResult(snapshot, validationErrors);
    }

    public static ProxyConfigLintSubmittedConfigurationResult Failed(ProxyConfigLintSubmittedConfigurationFailureKind kind, string message)
    {
        return new FailedResult(new ProxyConfigLintSubmittedConfigurationFailure(kind, message));
    }

    public static ProxyConfigLintSubmittedConfigurationResult Empty()
    {
        return EmptyResult.Instance;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record LoadedResult : ProxyConfigLintSubmittedConfigurationResult
    {
        public LoadedResult(ProxyConfigLintConfigurationSnapshot snapshot, IReadOnlyList<ProxyConfigurationFileError> validationErrors)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(validationErrors);
            Snapshot = snapshot;
            ValidationErrors = ConfigLintList.Copy(validationErrors);
        }

        public ProxyConfigLintConfigurationSnapshot Snapshot { get; }
        public IReadOnlyList<ProxyConfigurationFileError> ValidationErrors { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record FailedResult : ProxyConfigLintSubmittedConfigurationResult
    {
        public FailedResult(ProxyConfigLintSubmittedConfigurationFailure failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public ProxyConfigLintSubmittedConfigurationFailure Failure { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record EmptyResult : ProxyConfigLintSubmittedConfigurationResult
    {
        public static EmptyResult Instance { get; } = new();

        private EmptyResult()
        {
        }
    }
}
