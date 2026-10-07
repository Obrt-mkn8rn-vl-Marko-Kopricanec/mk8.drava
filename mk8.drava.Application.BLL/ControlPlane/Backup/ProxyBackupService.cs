using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;

namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed class ProxyBackupService : IProxyBackupOperations
{
    private const int MaxEntries = 256;
    private const int MaxWarnings = 64;
    private readonly IMdravaDataDirectoryProvider _dataDirectoryProvider;
    private readonly IProxyBackupFileSystem _backupFileSystem;
    private readonly IProxyRestoreConfigurationValidator _restoreConfigurationValidator;
    private readonly IProxyActiveConfigurationVersionReader _activeConfigurationVersionReader;
    private readonly TimeProvider _timeProvider;
    public ProxyBackupService(IMdravaDataDirectoryProvider dataDirectoryProvider, IProxyBackupFileSystem backupFileSystem, IProxyRestoreConfigurationValidator restoreConfigurationValidator, IProxyActiveConfigurationVersionReader activeConfigurationVersionReader, TimeProvider timeProvider)
    {
        _dataDirectoryProvider = dataDirectoryProvider;
        _backupFileSystem = backupFileSystem;
        _restoreConfigurationValidator = restoreConfigurationValidator;
        _activeConfigurationVersionReader = activeConfigurationVersionReader;
        _timeProvider = timeProvider;
    }

    public ProxyBackupManifest CreateManifest()
    {
        var generatedAtUtc = _timeProvider.GetUtcNow();
        var root = _dataDirectoryProvider.GetDataDirectory();
        var directories = ExpectedDirectories(root);
        List<ProxyBackupManifestEntry> entries = [];
        List<ProxyBackupWarning> warnings = [];
        var scan = _backupFileSystem.ScanDataDirectory(root);
        if (!scan.RootExists)
        {
            warnings.Add(ProxyBackupWarningPolicy.DataDirectoryMissing());
        }
        else
        {
            foreach (var file in scan.Files)
            {
                entries.Add(ProxyBackupManifestEntryFactory.FromFileSystemEntry(file));
            }

            foreach (var warning in scan.Warnings)
            {
                warnings.Add(ProxyBackupWarningPolicy.FromFileSystemWarning(warning));
            }
        }

        foreach (var directory in directories.Where(static directory => !directory.Exists))
        {
            warnings.Add(ProxyBackupWarningPolicy.MissingDirectory(directory.RelativePath));
        }

        return ProxyBackupManifestBuilder.Build(generatedAtUtc, directories, entries, warnings, MaxEntries, MaxWarnings);
    }

    public async ValueTask<ProxyRestoreValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var generatedAtUtc = _timeProvider.GetUtcNow();
        var root = _dataDirectoryProvider.GetDataDirectory();
        var manifest = CreateManifest();
        List<ProxyRestoreValidationFinding> errors = [];
        List<ProxyRestoreValidationFinding> warnings = manifest.Warnings.Select(ProxyRestoreValidationFindingPolicy.FromBackupWarning).ToList();
        errors.AddRange(ProxyRestoreValidationDirectoryPolicy.FindMissingRequiredDirectories(manifest.Directories));
        var configValidation = await _restoreConfigurationValidator.ValidateExistingLayoutAsync(cancellationToken).ConfigureAwait(false);
        foreach (var fileError in configValidation.FileErrors)
        {
            errors.Add(ProxyRestoreValidationFindingPolicy.ConfigurationError(fileError.Message, SafeRelativeOrNull(root, fileError.Path)));
        }

        foreach (var error in configValidation.Errors.Except(configValidation.FileErrors.Select(static fileError => fileError.Message), StringComparer.Ordinal))
        {
            errors.Add(ProxyRestoreValidationFindingPolicy.ConfigurationError(error, relativePath: null));
        }

        return ProxyRestoreValidationResultBuilder.Build(generatedAtUtc, _activeConfigurationVersionReader.ActiveConfigVersion, configValidation, manifest, errors, warnings, MaxWarnings);
    }

    private IReadOnlyList<ProxyBackupDirectoryStatus> ExpectedDirectories(string root)
    {
        return ProxyBackupDirectoryLayoutPolicy.ExpectedDirectories().Select(requirement => DirectoryStatus(root, requirement)).ToArray();
    }

    private ProxyBackupDirectoryStatus DirectoryStatus(string root, ProxyBackupDirectoryRequirement requirement)
    {
        return new ProxyBackupDirectoryStatus(requirement.RelativePath, _backupFileSystem.DirectoryExists(root, requirement.RelativePath), requirement.Classification, requirement.Sensitive);
    }

    private string? SafeRelativeOrNull(string root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return _backupFileSystem.GetSafeRelativePath(root, path)is ProxySafeRelativePathResult.SafeResult safePath ? safePath.RelativePath : null;
    }
}
