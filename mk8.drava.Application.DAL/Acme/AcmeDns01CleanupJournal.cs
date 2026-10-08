using System.Text.Json;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;

namespace Mk8.Drava.Application.DAL.Acme;

public sealed class AcmeDns01CleanupJournal : IAcmeDns01CleanupJournal, IDisposable
{
    private const int MaximumEntries = 8;
    private readonly string _path;
    private readonly FileStream _lock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AcmeDns01CleanupEntry[] _entries;

    public AcmeDns01CleanupJournal(string path)
    {
        _path = path;
        var lockPath = path + ".lock";
        PrivateCertificateFile.ValidatePath(path); PrivateCertificateFile.ValidatePath(lockPath);
        if (!OperatingSystem.IsWindows() && File.Exists(lockPath) && (File.GetUnixFileMode(lockPath) & ~ (UnixFileMode.UserRead | UnixFileMode.UserWrite)) != UnixFileMode.None)
            throw new InvalidDataException("Cleanup journal lock must be private to its owner.");
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        _lock = new FileStream(lockPath, options);
        try { _entries = File.Exists(path) ? Load(PrivateCertificateFile.ReadProtected(path, 1, 65536)) : []; }
        catch { _lock.Dispose(); _gate.Dispose(); throw; }
    }

    public IReadOnlyList<AcmeDns01CleanupEntry> Read() => Array.AsReadOnly(Volatile.Read(ref _entries));

    public async ValueTask PutAsync(AcmeDns01CleanupEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _entries;
            var index = Array.FindIndex(current, item => string.Equals(item.OperationId, entry.OperationId, StringComparison.Ordinal));
            if (index < 0 && current.Length == MaximumEntries) throw new InvalidDataException("Cleanup journal is full; unresolved challenges require recovery.");
            if (index >= 0) RequireSameIntent(current[index], entry);
            var next = new AcmeDns01CleanupEntry[current.Length + (index < 0 ? 1 : 0)];
            current.CopyTo(next, 0); next[index < 0 ? current.Length : index] = entry;
            await SaveAsync(next, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask RemoveAsync(string operationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _entries;
            var index = Array.FindIndex(current, item => string.Equals(item.OperationId, operationId, StringComparison.Ordinal));
            if (index < 0) throw new InvalidDataException("Cleanup journal operation is absent.");
            var next = new AcmeDns01CleanupEntry[current.Length - 1];
            Array.Copy(current, 0, next, 0, index); Array.Copy(current, index + 1, next, index, current.Length - index - 1);
            await SaveAsync(next, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async ValueTask SaveAsync(AcmeDns01CleanupEntry[] entries, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, entries });
        await PrivateCertificateFile.WriteProtectedAsync(_path, bytes, overwrite: true, 1, 65536, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _entries, entries);
    }

    private static void RequireSameIntent(AcmeDns01CleanupEntry previous, AcmeDns01CleanupEntry next)
    {
        if (!string.Equals(previous.ZoneId, next.ZoneId, StringComparison.Ordinal) || !string.Equals(previous.SiteId, next.SiteId, StringComparison.Ordinal) ||
            !string.Equals(previous.SiteDomain, next.SiteDomain, StringComparison.Ordinal) || !string.Equals(previous.Host, next.Host, StringComparison.Ordinal) ||
            !string.Equals(previous.Value, next.Value, StringComparison.Ordinal) ||
            (previous.RecordId.Length != 0 && !string.Equals(previous.RecordId, next.RecordId, StringComparison.Ordinal)))
            throw new InvalidDataException("Cleanup journal cannot replace an operation's immutable ownership.");
    }

    private static AcmeDns01CleanupEntry[] Load(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        RequireProperties(root, ["version", "entries"]);
        if (root.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported cleanup journal version.");
        var entries = root.GetProperty("entries");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > MaximumEntries) throw new InvalidDataException("Cleanup journal exceeds its entry bound.");
        var result = new AcmeDns01CleanupEntry[entries.GetArrayLength()];
        var operations = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in entries.EnumerateArray())
        {
            RequireProperties(item, ["ZoneId", "SiteDomain", "SiteId", "OperationId", "Host", "Value", "RecordId"]);
            var entry = new AcmeDns01CleanupEntry(Text(item, "ZoneId"), Text(item, "SiteDomain"), Text(item, "SiteId"), Text(item, "OperationId"), Text(item, "Host"), Text(item, "Value"), Text(item, "RecordId"));
            if (!operations.Add(entry.OperationId)) throw new InvalidDataException("Cleanup journal repeats an operation.");
            result[index++] = entry;
        }
        return result;
    }

    private static string Text(JsonElement item, string property) => item.GetProperty(property).GetString() ?? throw new InvalidDataException("Cleanup journal field is absent.");

    private static void RequireProperties(JsonElement item, string[] properties)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Cleanup journal requires objects.");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
            if (Array.IndexOf(properties, property.Name) < 0 || !found.Add(property.Name)) throw new InvalidDataException("Cleanup journal contains unknown or duplicate properties.");
        if (found.Count != properties.Length) throw new InvalidDataException("Cleanup journal field is absent.");
    }

    public void Dispose() { _lock.Dispose(); _gate.Dispose(); }
}
