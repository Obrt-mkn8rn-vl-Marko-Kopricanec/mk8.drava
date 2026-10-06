using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public abstract record ProxyRestoreConfigurationValidationResult
{
    private ProxyRestoreConfigurationValidationResult()
    {
    }

    public abstract IReadOnlyList<string> Errors { get; }
    public abstract IReadOnlyList<ProxyConfigurationFileError> FileErrors { get; }
    public abstract int? WouldBeVersion { get; }

    public static ProxyRestoreConfigurationValidationResult Completed(IEnumerable<string> errors, IEnumerable<ProxyConfigurationFileError> fileErrors, int? wouldBeVersion)
    {
        ThrowIfNonPositive(wouldBeVersion, nameof(wouldBeVersion));
        var ownedErrors = BackupList.Copy(errors);
        var ownedFileErrors = BackupList.Copy(fileErrors);
        return ownedErrors.Count == 0 && ownedFileErrors.Count == 0 ? new ValidResult(wouldBeVersion) : new InvalidResult(ownedErrors, ownedFileErrors, wouldBeVersion);
    }

    private static void ThrowIfNonPositive(int? value, string paramName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record ValidResult : ProxyRestoreConfigurationValidationResult
    {
        public ValidResult(int? wouldBeVersion)
        {
            WouldBeVersion = wouldBeVersion;
        }

        public override IReadOnlyList<string> Errors => BackupList.Copy(Array.Empty<string>());
        public override IReadOnlyList<ProxyConfigurationFileError> FileErrors => BackupList.Copy(Array.Empty<ProxyConfigurationFileError>());
        public override int? WouldBeVersion { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record InvalidResult : ProxyRestoreConfigurationValidationResult
    {
        public InvalidResult(IEnumerable<string> errors, IEnumerable<ProxyConfigurationFileError> fileErrors, int? wouldBeVersion)
        {
            var ownedErrors = BackupList.Copy(errors);
            var ownedFileErrors = BackupList.Copy(fileErrors);
            if (ownedErrors.Count == 0 && ownedFileErrors.Count == 0)
            {
                throw new ArgumentException("Invalid restore configuration validation requires at least one error.");
            }

            Errors = ownedErrors;
            FileErrors = ownedFileErrors;
            WouldBeVersion = wouldBeVersion;
        }

        public override IReadOnlyList<string> Errors { get; }
        public override IReadOnlyList<ProxyConfigurationFileError> FileErrors { get; }
        public override int? WouldBeVersion { get; }
    }
}
