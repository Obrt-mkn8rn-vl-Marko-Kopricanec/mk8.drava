using System.Text.Json;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.DAL.DataDirectory;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class BackupRestoreTests
{
    public static void BackupManifestClassifiesDataDirectoryWithoutFileContentsOrSecrets()
    {
        const string secret = "phase-51-secret-value";
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "config", "sites"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "logs"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "certs", "acme", "private-keys", "home"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "certs", "acme", "metadata", "home"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "state"));
        File.WriteAllText(Path.Combine(temp.Path, "config", "proxy.json"), $"{{ \"token\": \"{secret}\" }}");
        File.WriteAllText(Path.Combine(temp.Path, "config", "sites", "home.json"), "{ \"name\": \"home\" }");
        File.WriteAllText(Path.Combine(temp.Path, "logs", "access.log"), $"Authorization: Bearer {secret}");
        File.WriteAllText(Path.Combine(temp.Path, "certs", "manual.pfx"), secret);
        File.WriteAllText(Path.Combine(temp.Path, "certs", "acme", "private-keys", "home", "current.pfx"), secret);
        File.WriteAllText(Path.Combine(temp.Path, "certs", "acme", "metadata", "home", "status.json"), "{ \"id\": \"home\" }");
        File.WriteAllText(Path.Combine(temp.Path, "state", "runtime.json"), secret);
        var generatedAtUtc = new DateTimeOffset(2026, 6, 10, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateService(temp.Path, timeProvider: new FixedTimeProvider(generatedAtUtc)).CreateManifest();
        var text = JsonSerializer.Serialize(manifest);
        AssertEx.Equal(generatedAtUtc, manifest.GeneratedAtUtc);
        AssertEx.True(manifest.Entries.All(static entry => !Path.IsPathRooted(entry.RelativePath)));
        AssertEx.True(manifest.Entries.All(static entry => !entry.RelativePath.StartsWith("..", StringComparison.Ordinal)));
        AssertEx.True(manifest.Entries.Any(static entry => string.Equals(entry.RelativePath, "config/proxy.json", StringComparison.Ordinal) && string.Equals(entry.Category, "config", StringComparison.Ordinal) && string.Equals(entry.Classification, "must_backup", StringComparison.Ordinal) && !entry.Sensitive));
        AssertEx.True(manifest.Entries.Any(static entry => string.Equals(entry.RelativePath, "logs/access.log", StringComparison.Ordinal) && string.Equals(entry.Category, "logs", StringComparison.Ordinal) && string.Equals(entry.Classification, "should_backup", StringComparison.Ordinal)));
        AssertEx.True(manifest.Entries.Any(static entry => string.Equals(entry.RelativePath, "certs/manual.pfx", StringComparison.Ordinal) && string.Equals(entry.Category, "manual_certificate_material", StringComparison.Ordinal) && string.Equals(entry.Classification, "never_export_by_default_sensitive", StringComparison.Ordinal) && entry.Sensitive));
        AssertEx.True(manifest.Entries.Any(static entry => string.Equals(entry.RelativePath, "certs/acme/private-keys/home/current.pfx", StringComparison.Ordinal) && string.Equals(entry.Category, "acme_secret_material", StringComparison.Ordinal) && entry.Sensitive));
        AssertEx.True(manifest.Entries.Any(static entry => string.Equals(entry.RelativePath, "state/runtime.json", StringComparison.Ordinal) && string.Equals(entry.Category, "state", StringComparison.Ordinal)));
        AssertEx.False(text.Contains(secret, StringComparison.Ordinal), text);
        AssertEx.False(text.Contains("Authorization", StringComparison.OrdinalIgnoreCase), text);
    }

    public static void BackupManifestReportsMissingDirectoriesWithBoundedWarnings()
    {
        using var temp = TemporaryDirectory.Create();
        var manifest = CreateService(temp.Path).CreateManifest();
        AssertEx.True(manifest.Directories.Any(static directory => string.Equals(directory.RelativePath, "config", StringComparison.Ordinal) && !directory.Exists));
        AssertEx.True(manifest.Directories.Any(static directory => string.Equals(directory.RelativePath, "logs", StringComparison.Ordinal) && !directory.Exists));
        AssertEx.True(manifest.Warnings.Count <= 64);
        AssertEx.True(manifest.Warnings.Any(static warning => string.Equals(warning.Code, "missing_directory", StringComparison.Ordinal)));
        AssertEx.True(manifest.Warnings.All(static warning => warning.RelativePath is null || !Path.IsPathRooted(warning.RelativePath)));
    }

    public static void BackupManifestBuilderTruncatesAndCountsBoundedEntries()
    {
        var generatedAtUtc = new DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);
        ProxyBackupManifestEntry[] entries = [new ProxyBackupManifestEntry("logs/z.log", "logs", "should_backup", false, 7, generatedAtUtc), new ProxyBackupManifestEntry("config/a.json", "config", "must_backup", false, 3, generatedAtUtc), new ProxyBackupManifestEntry("config/b.json", "config", "must_backup", false, 5, generatedAtUtc)];
        ProxyBackupWarning[] warnings = [new ProxyBackupWarning("existing_warning", "Existing warning.", null)];
        var manifest = ProxyBackupManifestBuilder.Build(generatedAtUtc, [], entries, warnings, maxEntries: 2, maxWarnings: 2);
        AssertEx.True(manifest.Truncated);
        AssertEx.Equal(2, manifest.Entries.Count);
        AssertEx.Equal("config/a.json", manifest.Entries[0].RelativePath);
        AssertEx.Equal("config/b.json", manifest.Entries[1].RelativePath);
        AssertEx.Equal(1, manifest.Counts.Count);
        AssertEx.Equal("config", manifest.Counts[0].Category);
        AssertEx.Equal("must_backup", manifest.Counts[0].Classification);
        AssertEx.Equal(2, manifest.Counts[0].Count);
        AssertEx.Equal(8, manifest.Counts[0].SizeBytes);
        AssertEx.Equal(2, manifest.Warnings.Count);
        AssertEx.Equal("existing_warning", manifest.Warnings[0].Code);
        AssertEx.Equal("manifest_truncated", manifest.Warnings[1].Code);
    }

    public static void BackupManifestEntryFactoryClassifiesFilesystemEntry()
    {
        var lastWriteTimeUtc = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
        var file = new ProxyBackupFileSystemEntry("certs/acme/private-keys/home/current.pfx", SizeBytes: 1234, lastWriteTimeUtc);
        var entry = ProxyBackupManifestEntryFactory.FromFileSystemEntry(file);
        AssertEx.Equal("certs/acme/private-keys/home/current.pfx", entry.RelativePath);
        AssertEx.Equal("acme_secret_material", entry.Category);
        AssertEx.Equal("never_export_by_default_sensitive", entry.Classification);
        AssertEx.True(entry.Sensitive);
        AssertEx.Equal(1234, entry.SizeBytes);
        AssertEx.Equal(lastWriteTimeUtc, entry.LastWriteTimeUtc);
    }

    public static void BackupFileSystemScanResultNamesMissingRootAndScannedOutcomes()
    {
        var lastWriteTimeUtc = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
        var file = new ProxyBackupFileSystemEntry("config/proxy.json", 10, lastWriteTimeUtc);
        var warning = new ProxyBackupFileSystemWarning("directory_unreadable", "logs");
        var missing = ProxyBackupFileSystemScanResult.MissingRoot();
        var scanned = ProxyBackupFileSystemScanResult.Scanned([file], [warning]);
        AssertEx.False(missing.RootExists);
        AssertEx.Equal(0, missing.Files.Count);
        AssertEx.Equal(0, missing.Warnings.Count);
        AssertEx.True(scanned.RootExists);
        AssertEx.Equal(file, scanned.Files[0]);
        AssertEx.Equal(warning, scanned.Warnings[0]);
    }

    public static void RestoreValidationDirectoryPolicyReportsOnlyMissingRequiredDirectories()
    {
        ProxyBackupDirectoryStatus[] directories = [new ProxyBackupDirectoryStatus("config", false, "must_backup", false), new ProxyBackupDirectoryStatus("logs", false, "should_backup", false), new ProxyBackupDirectoryStatus("config/sites", true, "must_backup", false)];
        var findings = ProxyRestoreValidationDirectoryPolicy.FindMissingRequiredDirectories(directories);
        AssertEx.Equal(1, findings.Count);
        AssertEx.Equal(ProxyStatusText.Error, findings[0].Severity);
        AssertEx.Equal("required_directory_missing", findings[0].Code);
        AssertEx.Equal("A required restore directory is missing.", findings[0].Message);
        AssertEx.Equal("config", findings[0].RelativePath);
    }

    public static void RestoreValidationFindingPolicyBuildsConfigurationErrorFindings()
    {
        var finding = ProxyRestoreValidationFindingPolicy.ConfigurationError("Certificate file does not exist.", "certs/home.pfx");
        AssertEx.Equal(ProxyStatusText.Error, finding.Severity);
        AssertEx.Equal("certificate_file_missing", finding.Code);
        AssertEx.Equal("Referenced certificate material is missing.", finding.Message);
        AssertEx.Equal("certs/home.pfx", finding.RelativePath);
    }

    public static void RestoreValidationResponseBuilderBoundsFindingsAndRequiresNoErrors()
    {
        var generatedAtUtc = new DateTimeOffset(2026, 6, 12, 11, 0, 0, TimeSpan.Zero);
        var manifest = new ProxyBackupManifest(generatedAtUtc, [], [], [], [], Truncated: false);
        var configValidation = ProxyRestoreConfigurationValidationResult.Completed(errors: [], fileErrors: [], wouldBeVersion: 7);
        var invalidConfigValidation = ProxyRestoreConfigurationValidationResult.Completed(errors: ["parse failed"], fileErrors: [], wouldBeVersion: null);
        ProxyRestoreValidationFinding[] errors = [new ProxyRestoreValidationFinding(ProxyStatusText.Error, "first", "First.", null), new ProxyRestoreValidationFinding(ProxyStatusText.Error, "second", "Second.", null)];
        ProxyRestoreValidationFinding[] warnings = [new ProxyRestoreValidationFinding(ProxyStatusText.Warning, "first_warning", "First warning.", null), new ProxyRestoreValidationFinding(ProxyStatusText.Warning, "second_warning", "Second warning.", null)];
        var response = ProxyRestoreValidationResultBuilder.Build(generatedAtUtc, activeConfigVersion: 3, configValidation, manifest, errors, warnings, maxFindings: 1);
        var invalidResponse = ProxyRestoreValidationResultBuilder.Build(generatedAtUtc, activeConfigVersion: 3, invalidConfigValidation, manifest, [], [], maxFindings: 1);
        AssertEx.True(configValidation is ProxyRestoreConfigurationValidationResult.ValidResult);
        AssertEx.True(invalidConfigValidation is ProxyRestoreConfigurationValidationResult.InvalidResult);
        AssertRejected(invalidResponse);
        AssertEx.False(invalidResponse.ConfigValidationSucceeded);
        AssertEx.Equal("parse failed", invalidConfigValidation.Errors[0]);
        AssertRejected(response);
        AssertEx.Equal(generatedAtUtc, response.GeneratedAtUtc);
        AssertEx.Equal(3, response.ActiveConfigVersion);
        AssertEx.True(response.ConfigValidationSucceeded);
        AssertEx.Equal(7, response.WouldBeConfigVersion);
        AssertEx.Equal(1, response.Errors.Count);
        AssertEx.Equal("first", response.Errors[0].Code);
        AssertEx.Equal(1, response.Warnings.Count);
        AssertEx.Equal("first_warning", response.Warnings[0].Code);
    }

    public static void BackupAndRestoreResultsCopyInputCollections()
    {
        var generatedAtUtc = new DateTimeOffset(2026, 6, 12, 11, 30, 0, TimeSpan.Zero);
        var file = new ProxyBackupFileSystemEntry("config/proxy.json", 10, generatedAtUtc);
        var warning = new ProxyBackupFileSystemWarning("directory_unreadable", "logs");
        var scanFiles = new List<ProxyBackupFileSystemEntry>
        {
            file
        };
        var scanWarnings = new List<ProxyBackupFileSystemWarning>
        {
            warning
        };
        var configErrors = new List<string>
        {
            "parse failed"
        };
        var configFileErrors = new List<ProxyConfigurationFileError>
        {
            ProxyConfigurationFileError.ForPath("config/proxy.json", "parse failed")
        };
        var restoreErrors = new List<ProxyRestoreValidationFinding>
        {
            new(ProxyStatusText.Error, "config_invalid", "Configuration invalid.", "config/proxy.json")
        };
        var restoreWarnings = new List<ProxyRestoreValidationFinding>
        {
            new(ProxyStatusText.Warning, "manifest_warning", "Manifest warning.", null)
        };
        var manifestDirectories = new List<ProxyBackupDirectoryStatus>
        {
            new("config", true, "must_backup", true)
        };
        var manifestEntries = new List<ProxyBackupManifestEntry>
        {
            new("config/proxy.json", "config", "must_backup", false, 10, generatedAtUtc)
        };
        var manifestCounts = new List<ProxyBackupManifestCount>
        {
            new("config", "must_backup", 1, 10)
        };
        var manifestWarnings = new List<ProxyBackupWarning>
        {
            new("manifest_warning", "Manifest warning.", null)
        };
        var manifest = new ProxyBackupManifest(generatedAtUtc, manifestDirectories, manifestEntries, manifestCounts, manifestWarnings, Truncated: false);
        var scan = ProxyBackupFileSystemScanResult.Scanned(scanFiles.Select(static item => item), scanWarnings.Select(static item => item));
        var configValidation = ProxyRestoreConfigurationValidationResult.Completed(configErrors.Select(static item => item), configFileErrors.Select(static item => item), wouldBeVersion: null);
        var restoreValidation = ProxyRestoreValidationResult.Completed(generatedAtUtc, activeConfigVersion: 3, configValidation, manifest, restoreErrors.Select(static item => item), restoreWarnings.Select(static item => item));
        scanFiles.Clear();
        scanWarnings.Clear();
        configErrors.Clear();
        configFileErrors.Clear();
        manifestDirectories.Clear();
        manifestEntries.Clear();
        manifestCounts.Clear();
        manifestWarnings.Clear();
        restoreErrors.Clear();
        restoreWarnings.Clear();
        AssertEx.Equal("config/proxy.json", scan.Files[0].RelativePath);
        AssertEx.Equal("directory_unreadable", scan.Warnings[0].Code);
        AssertEx.Equal("parse failed", configValidation.Errors[0]);
        AssertEx.Equal("config/proxy.json", configValidation.FileErrors[0].Path);
        AssertEx.Equal("config", manifest.Directories[0].RelativePath);
        AssertEx.Equal("config/proxy.json", manifest.Entries[0].RelativePath);
        AssertEx.Equal("config", manifest.Counts[0].Category);
        AssertEx.Equal("manifest_warning", manifest.Warnings[0].Code);
        AssertEx.Equal("config_invalid", restoreValidation.Errors[0].Code);
        AssertEx.Equal("manifest_warning", restoreValidation.Warnings[0].Code);
        AssertEx.False(scan.Files is ProxyBackupFileSystemEntry[]);
        AssertEx.False(scan.Warnings is ProxyBackupFileSystemWarning[]);
        AssertEx.False(configValidation.Errors is string[]);
        AssertEx.False(configValidation.FileErrors is ProxyConfigurationFileError[]);
        AssertEx.False(manifest.Directories is ProxyBackupDirectoryStatus[]);
        AssertEx.False(manifest.Entries is ProxyBackupManifestEntry[]);
        AssertEx.False(manifest.Counts is ProxyBackupManifestCount[]);
        AssertEx.False(manifest.Warnings is ProxyBackupWarning[]);
        AssertEx.False(restoreValidation.Errors is ProxyRestoreValidationFinding[]);
        AssertEx.False(restoreValidation.Warnings is ProxyRestoreValidationFinding[]);
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupFileSystemEntry("", 10, generatedAtUtc));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyBackupFileSystemEntry("config/proxy.json", -1, generatedAtUtc));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupManifestEntry("", "config", "must_backup", false, 10, generatedAtUtc));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupManifestEntry("config/proxy.json", "", "must_backup", false, 10, generatedAtUtc));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupManifestEntry("config/proxy.json", "config", "", false, 10, generatedAtUtc));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyBackupManifestEntry("config/proxy.json", "config", "must_backup", false, -1, generatedAtUtc));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupFileClassification("", "must_backup", false));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupFileClassification("config", "", false));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupDirectoryRequirement("", "must_backup", false));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupDirectoryRequirement("config", "", false));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupDirectoryStatus("", true, "must_backup", false));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupDirectoryStatus("config", true, "", false));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupManifestCount("", "must_backup", 1, 10));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupManifestCount("config", "", 1, 10));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyBackupManifestCount("config", "must_backup", -1, 10));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyBackupManifestCount("config", "must_backup", 1, -1));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupWarning("", "Manifest warning.", null));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupWarning("manifest_warning", "", null));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupWarning("manifest_warning", "Manifest warning.", ""));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupFileSystemWarning("", "logs"));
        AssertEx.Throws<ArgumentException>(() => new ProxyBackupFileSystemWarning("directory_unreadable", ""));
        AssertEx.Throws<ArgumentException>(() => new ProxyRestoreValidationFindingShape("", "Finding."));
        AssertEx.Throws<ArgumentException>(() => new ProxyRestoreValidationFindingShape("finding", ""));
        AssertEx.Throws<ArgumentException>(() => new ProxyRestoreValidationFinding("", "finding", "Finding.", null));
        AssertEx.Throws<ArgumentException>(() => new ProxyRestoreValidationFinding(ProxyStatusText.Error, "", "Finding.", null));
        AssertEx.Throws<ArgumentException>(() => new ProxyRestoreValidationFinding(ProxyStatusText.Error, "finding", "", null));
        AssertEx.Throws<ArgumentException>(() => new ProxyRestoreValidationFinding(ProxyStatusText.Error, "finding", "Finding.", ""));
        AssertEx.Throws<ArgumentNullException>(() => ProxyBackupFileSystemScanResult.Scanned([null!], []));
        AssertEx.Throws<ArgumentNullException>(() => ProxyBackupFileSystemScanResult.Scanned([], [null!]));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyBackupManifest(generatedAtUtc, [null!], [], [], [], Truncated: false));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRestoreConfigurationValidationResult.Completed([null!], [], wouldBeVersion: null));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRestoreConfigurationValidationResult.Completed([], [null!], wouldBeVersion: null));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyRestoreConfigurationValidationResult.Completed([], [], wouldBeVersion: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyRestoreConfigurationValidationResult.Completed(["parse failed"], [], wouldBeVersion: -1));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRestoreValidationResult.Completed(generatedAtUtc, activeConfigVersion: 3, configValidation, manifest, [null!], []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyRestoreValidationResult.Completed(generatedAtUtc, activeConfigVersion: 0, configValidation, manifest, [], []));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRestoreValidationResult.Completed(generatedAtUtc, activeConfigVersion: 3, configValidation, manifest, restoreErrors, [null!]));
        var response = ProxyBackupManifestResponseMapper.FromManifest(manifest);
        AssertEx.False(response.Directories is ProxyBackupDirectoryStatusResponse[], "Backup manifest API directories should not expose a mutable array.");
        AssertEx.False(response.Entries is ProxyBackupManifestEntryResponse[], "Backup manifest API entries should not expose a mutable array.");
        AssertEx.False(response.Counts is ProxyBackupManifestCountResponse[], "Backup manifest API counts should not expose a mutable array.");
        AssertEx.False(response.Warnings is ProxyBackupWarningResponse[], "Backup manifest API warnings should not expose a mutable array.");
        var validationResponse = ProxyRestoreValidationResponseBodyMapper.FromResult(restoreValidation);
        AssertEx.False(validationResponse.Errors is ProxyRestoreValidationFindingResponse[], "Restore validation API errors should not expose a mutable array.");
        AssertEx.False(validationResponse.Warnings is ProxyRestoreValidationFindingResponse[], "Restore validation API warnings should not expose a mutable array.");
        var responseDirectories = new List<ProxyBackupDirectoryStatusResponse>
        {
            response.Directories[0]
        };
        var responseEntries = new List<ProxyBackupManifestEntryResponse>
        {
            response.Entries[0]
        };
        var responseCounts = new List<ProxyBackupManifestCountResponse>
        {
            response.Counts[0]
        };
        var responseWarnings = new List<ProxyBackupWarningResponse>
        {
            response.Warnings[0]
        };
        var directManifestResponse = new ProxyBackupManifestResponse(generatedAtUtc: generatedAtUtc, directories: responseDirectories, entries: responseEntries, counts: responseCounts, warnings: responseWarnings, truncated: false);
        var responseErrors = new List<ProxyRestoreValidationFindingResponse>
        {
            validationResponse.Errors[0]
        };
        var restoreResponseWarnings = new List<ProxyRestoreValidationFindingResponse>
        {
            validationResponse.Warnings[0]
        };
        var directValidationResponse = new ProxyRestoreValidationResponseBody(succeeded: false, generatedAtUtc, activeConfigVersion: 3, configValidationSucceeded: false, wouldBeConfigVersion: null, manifest: directManifestResponse, errors: responseErrors, warnings: restoreResponseWarnings);
        responseDirectories[0] = responseDirectories[0] with
        {
            RelativePath = "replacement-config"
        };
        responseEntries[0] = responseEntries[0] with
        {
            RelativePath = "replacement-entry"
        };
        responseCounts[0] = responseCounts[0] with
        {
            Category = "replacement-count"
        };
        responseWarnings[0] = responseWarnings[0] with
        {
            Code = "replacement-warning"
        };
        responseErrors[0] = responseErrors[0] with
        {
            Code = "replacement-error"
        };
        restoreResponseWarnings[0] = restoreResponseWarnings[0] with
        {
            Code = "replacement-restore-warning"
        };
        responseDirectories.Clear();
        responseEntries.Clear();
        responseCounts.Clear();
        responseWarnings.Clear();
        responseErrors.Clear();
        restoreResponseWarnings.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyBackupManifestResponse(generatedAtUtc: generatedAtUtc, directories: null!, entries: [], counts: [], warnings: [], truncated: false));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRestoreValidationResponseBody(succeeded: false, generatedAtUtc, activeConfigVersion: 3, configValidationSucceeded: false, wouldBeConfigVersion: null, manifest: null!, [], []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRestoreValidationResponseBody(succeeded: false, generatedAtUtc, activeConfigVersion: 3, configValidationSucceeded: false, wouldBeConfigVersion: null, manifest: directManifestResponse, errors: null!, warnings: []));
        AssertEx.Equal("config", directManifestResponse.Directories[0].RelativePath);
        AssertEx.Equal("config/proxy.json", directManifestResponse.Entries[0].RelativePath);
        AssertEx.Equal("config", directManifestResponse.Counts[0].Category);
        AssertEx.Equal("manifest_warning", directManifestResponse.Warnings[0].Code);
        AssertEx.Equal("config_invalid", directValidationResponse.Errors[0].Code);
        AssertEx.Equal("manifest_warning", directValidationResponse.Warnings[0].Code);
        AssertEx.False(directManifestResponse.Directories is ProxyBackupDirectoryStatusResponse[], "Direct backup manifest API directories should not expose a mutable array.");
        AssertEx.False(directValidationResponse.Errors is ProxyRestoreValidationFindingResponse[], "Direct restore validation API errors should not expose a mutable array.");
    }

    public static void BackupPathSafetyRejectsTraversalOutsideDataDirectory()
    {
        using var temp = TemporaryDirectory.Create();
        var inside = Path.Combine(temp.Path, "config", "proxy.json");
        var outside = Path.Combine(temp.Path, "..", "outside.json");
        var pathSafety = new ProxyDataDirectoryPathSafety();
        var insideResult = pathSafety.GetSafeRelativePath(temp.Path, inside);
        AssertEx.True(insideResult is ProxySafeRelativePathResult.SafeResult);
        var insideSafePath = (ProxySafeRelativePathResult.SafeResult)insideResult;
        AssertEx.Equal("config/proxy.json", insideSafePath.RelativePath);
        var outsideResult = pathSafety.GetSafeRelativePath(temp.Path, outside);
        AssertEx.True(outsideResult is ProxySafeRelativePathResult.UnsafeResult);
    }

    public static async Task RestoreValidationCatchesInvalidConfigWithoutCreatingBootstrapFilesAsync()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "config", "sites"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "sites", "broken.json"), "{ nope").ConfigureAwait(false);
        var result = await CreateService(temp.Path).ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        AssertRejected(result);
        AssertEx.True(result.Errors.Any(static error => string.Equals(error.Code, "config_parse_failed", StringComparison.Ordinal)), string.Join(",", result.Errors.Select(static error => error.Code)));
        AssertEx.False(File.Exists(Path.Combine(temp.Path, "config", "proxy.json")));
        AssertEx.False(File.Exists(Path.Combine(temp.Path, "config", "sites", "example.site.yaml")));
    }

    public static async Task RestoreValidationCatchesMissingReferencedCertificateMaterialAsync()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteHttpsSite(temp.Path, "home.json", port: 18443, upstreamPort: 15000, certificateId: "home-cert");
        ConfigurationTests.WriteOperationalConfig(temp.Path, certificateId: "home-cert", certificatePath: "certs/missing.pfx");
        var result = await CreateService(temp.Path).ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        AssertRejected(result);
        AssertEx.True(result.Errors.Any(static error => string.Equals(error.Code, "certificate_file_missing", StringComparison.Ordinal)), string.Join(",", result.Errors.Select(static error => error.Code)));
        AssertEx.False(JsonSerializer.Serialize(result).Contains("missing.pfx", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task RestoreValidationPreservesExistingActiveRuntimeStateAsync()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteSite(temp.Path, "home.json", port: 18080, upstreamPort: 15000);
        var store = new ProxyConfigurationStore();
        var loader = CreateLoader(temp.Path);
        var load = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        store.Replace(ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(load));
        var loadedAt = store.Snapshot.LoadedAtUtc;
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "sites", "broken.json"), "{ nope").ConfigureAwait(false);
        var result = await CreateService(temp.Path, store, loader).ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        AssertRejected(result);
        AssertEx.Equal(1, result.ActiveConfigVersion);
        AssertEx.Equal(1, store.Snapshot.Version);
        AssertEx.Equal(loadedAt, store.Snapshot.LoadedAtUtc);
    }

    public static async Task RestoreValidationSucceedsWithBootstrapLayoutAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var loader = CreateLoader(temp.Path);
        var load = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertLoaded(load);
        var generatedAtUtc = new DateTimeOffset(2026, 6, 10, 8, 5, 0, TimeSpan.Zero);
        var result = await CreateService(temp.Path, loader: loader, timeProvider: new FixedTimeProvider(generatedAtUtc)).ValidateAsync(CancellationToken.None).ConfigureAwait(false);
        AssertAccepted(result, string.Join(",", result.Errors.Select(static error => error.Code)));
        AssertEx.Equal(generatedAtUtc, result.GeneratedAtUtc);
        AssertEx.Equal(generatedAtUtc, result.Manifest.GeneratedAtUtc);
        AssertEx.True(result.Manifest.Directories.Where(static directory => !string.Equals(directory.RelativePath, "certs/acme", StringComparison.Ordinal)).All(static directory => directory.Exists));
        AssertEx.True(File.Exists(Path.Combine(temp.Path, "config", "proxy.json")));
        AssertEx.True(File.Exists(Path.Combine(temp.Path, "config", "sites", "example.site.yaml")));
    }

    private static ProxyBackupService CreateService(string dataDirectory, ProxyConfigurationStore? store = null, ProxyConfigurationLoader? loader = null, TimeProvider? timeProvider = null)
    {
        return new ProxyBackupService(Provider(dataDirectory), new ProxyBackupFileSystem(new ProxyDataDirectoryPathSafety()), loader ?? CreateLoader(dataDirectory), store ?? new ProxyConfigurationStore(), timeProvider ?? TimeProvider.System);
    }

    private static ProxyConfigurationLoader CreateLoader(string dataDirectory)
    {
        var provider = Provider(dataDirectory);
        return new ProxyConfigurationLoader(provider, new ProxyDataDirectoryBootstrapper(provider), new SiteConfigurationParser(), new Mk8.Drava.Application.INF.Configuration.ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, TimeProvider.System);
    }

    private static MdravaDataDirectoryProvider Provider(string dataDirectory)
    {
        return new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory });
    }

    private static void AssertAccepted(ProxyRestoreValidationResult result, string? message = null)
    {
        AssertEx.True(result is ProxyRestoreValidationResult.AcceptedResult, message ?? string.Join(",", result.Errors.Select(static error => error.Code)));
    }

    private static void AssertRejected(ProxyRestoreValidationResult result, string? message = null)
    {
        AssertEx.True(result is ProxyRestoreValidationResult.RejectedResult, message ?? string.Join(",", result.Errors.Select(static error => error.Code)));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-backup-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
