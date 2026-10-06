using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public abstract partial record ProxyConfigurationValidationResult
{
    private ProxyConfigurationValidationResult(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? lastSuccessfulLoadAtUtc, int? wouldBeVersion, IReadOnlyList<string> sourceFiles, ProxyConfigurationDiscovery discovery, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileError> fileErrors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(fileErrors);
        ThrowIfNonPositive(activeVersion, nameof(activeVersion));
        ThrowIfNonPositive(wouldBeVersion, nameof(wouldBeVersion));
        SourceDirectory = sourceDirectory;
        AttemptedAtUtc = attemptedAtUtc;
        ActiveVersion = activeVersion;
        LastSuccessfulLoadAtUtc = lastSuccessfulLoadAtUtc;
        WouldBeVersion = wouldBeVersion;
        SourceFiles = ConfigurationManagementList.Copy(sourceFiles);
        Discovery = discovery;
        Errors = ConfigurationManagementList.Copy(errors);
        FileErrors = ConfigurationManagementList.Copy(fileErrors);
    }

    public string SourceDirectory { get; }
    public DateTimeOffset AttemptedAtUtc { get; }
    public int? ActiveVersion { get; }
    public DateTimeOffset? LastSuccessfulLoadAtUtc { get; }
    public int? WouldBeVersion { get; }
    public IReadOnlyList<string> SourceFiles { get; }
    public ProxyConfigurationDiscovery Discovery { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<ProxyConfigurationFileError> FileErrors { get; }

    public static ProxyConfigurationValidationResult Valid(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? lastSuccessfulLoadAtUtc, int? wouldBeVersion, IReadOnlyList<string> sourceFiles, ProxyConfigurationDiscovery discovery)
    {
        return new ValidResult(sourceDirectory, attemptedAtUtc, activeVersion, lastSuccessfulLoadAtUtc, wouldBeVersion, sourceFiles, discovery);
    }

    public static ProxyConfigurationValidationResult Invalid(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? lastSuccessfulLoadAtUtc, int? wouldBeVersion, IReadOnlyList<string> sourceFiles, ProxyConfigurationDiscovery discovery, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileError> fileErrors)
    {
        return new InvalidResult(sourceDirectory, attemptedAtUtc, activeVersion, lastSuccessfulLoadAtUtc, wouldBeVersion, sourceFiles, discovery, errors, fileErrors);
    }

    private static void ThrowIfNonPositive(int? value, string paramName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record ValidResult : ProxyConfigurationValidationResult
    {
        internal ValidResult(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? lastSuccessfulLoadAtUtc, int? wouldBeVersion, IReadOnlyList<string> sourceFiles, ProxyConfigurationDiscovery discovery) : base(sourceDirectory, attemptedAtUtc, activeVersion, lastSuccessfulLoadAtUtc, wouldBeVersion, sourceFiles, discovery, [], [])
        {
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record InvalidResult : ProxyConfigurationValidationResult
    {
        internal InvalidResult(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? lastSuccessfulLoadAtUtc, int? wouldBeVersion, IReadOnlyList<string> sourceFiles, ProxyConfigurationDiscovery discovery, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileError> fileErrors) : base(sourceDirectory, attemptedAtUtc, activeVersion, lastSuccessfulLoadAtUtc, wouldBeVersion, sourceFiles, discovery, errors, fileErrors)
        {
        }
    }
}
