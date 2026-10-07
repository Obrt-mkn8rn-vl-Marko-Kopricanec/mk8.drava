using System.Collections.ObjectModel;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public abstract record ProxyConfigurationNormalizeResult
{
    private ProxyConfigurationNormalizeResult(string format, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileError> fileErrors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(fileErrors);
        Format = format;
        Errors = ConfigurationManagementList.Copy(errors);
        FileErrors = ConfigurationManagementList.Copy(fileErrors);
    }

    public string Format { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<ProxyConfigurationFileError> FileErrors { get; }

    public static ProxyConfigurationNormalizeResult Normalized(string format, string canonicalJson)
    {
        return new NormalizedResult(format, canonicalJson);
    }

    public static ProxyConfigurationNormalizeResult Failed(string format, IReadOnlyList<ProxyConfigurationFileError> fileErrors)
    {
        return new FailedResult(format, fileErrors);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record NormalizedResult : ProxyConfigurationNormalizeResult
    {
        internal NormalizedResult(string format, string canonicalJson) : base(format, [], [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
            CanonicalJson = canonicalJson;
        }

        public string CanonicalJson { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record FailedResult : ProxyConfigurationNormalizeResult
    {
        internal FailedResult(string format, IReadOnlyList<ProxyConfigurationFileError> fileErrors) : this(format, ConfigurationManagementList.Copy(fileErrors))
        {
        }

        private FailedResult(string format, ReadOnlyCollection<ProxyConfigurationFileError> fileErrors) : base(format, CreateErrors(fileErrors), fileErrors)
        {
            if (fileErrors.Count == 0)
            {
                throw new ArgumentException("A failed configuration normalize result requires at least one file error.", nameof(fileErrors));
            }
        }

        private static string[] CreateErrors(IReadOnlyList<ProxyConfigurationFileError> fileErrors)
        {
            ArgumentNullException.ThrowIfNull(fileErrors);
            return fileErrors.Select(static error => error.Path is null ? error.Message : $"{error.Path}: {error.Message}").ToArray();
        }
    }
}
