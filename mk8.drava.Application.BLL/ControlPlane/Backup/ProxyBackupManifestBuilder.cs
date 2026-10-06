namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public static class ProxyBackupManifestBuilder
{
    public static ProxyBackupManifest Build(DateTimeOffset generatedAtUtc, IEnumerable<ProxyBackupDirectoryStatus> directories, IEnumerable<ProxyBackupManifestEntry> entries, IEnumerable<ProxyBackupWarning> warnings, int maxEntries, int maxWarnings)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxWarnings);
        var sortedEntries = entries.OrderBy(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var truncated = sortedEntries.Length > maxEntries;
        var boundedEntries = truncated ? sortedEntries.Take(maxEntries).ToArray() : sortedEntries;
        var boundedWarnings = warnings.ToList();
        if (truncated && boundedWarnings.Count < maxWarnings)
        {
            boundedWarnings.Add(ProxyBackupWarningPolicy.ManifestTruncated());
        }

        var counts = boundedEntries.GroupBy(static entry => (entry.Category, entry.Classification)).Select(static group => new ProxyBackupManifestCount(group.Key.Category, group.Key.Classification, group.Count(), group.Sum(static entry => entry.SizeBytes))).OrderBy(static count => count.Category, StringComparer.OrdinalIgnoreCase).ThenBy(static count => count.Classification, StringComparer.OrdinalIgnoreCase).ToArray();
        return new ProxyBackupManifest(generatedAtUtc, directories, boundedEntries, counts, boundedWarnings.Take(maxWarnings), truncated);
    }
}
