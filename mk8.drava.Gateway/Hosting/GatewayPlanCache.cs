using Google.Protobuf;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Gateway.Hosting;

internal sealed class GatewayPlanCache : IDisposable
{
    private readonly string _directory;
    private readonly FileStream _lock;
    private string PlanPath => Path.Combine(_directory, "serving.plan");

    public GatewayPlanCache(string directory)
    {
        RequirePath(directory);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _directory = directory;
        var path = Path.Combine(directory, "gateway.lock");
        RequirePath(path);
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        _lock = new FileStream(path, options);
    }

    public async ValueTask<PresentationPlan?> ReadAsync(CancellationToken cancellationToken)
    {
        RequirePath(PlanPath);
        if (!File.Exists(PlanPath)) return null;
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(PlanPath) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != UnixFileMode.None) throw new InvalidDataException("Gateway material must be private.");
        var input = new FileStream(PlanPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, useAsync: true);
        await using var inputLifetime = input.ConfigureAwait(false);
        if (input.Length is < 128 or > 128 * 1024) throw new InvalidDataException("Cached presentation plan exceeds its bound.");
        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return PresentationPlan.Parser.ParseFrom(bytes);
    }

    public async ValueTask WriteAsync(PresentationPlan plan, CancellationToken cancellationToken)
    {
        RequirePath(PlanPath);
        var bytes = plan.ToByteArray();
        if (bytes.Length is < 128 or > 128 * 1024) throw new InvalidDataException("Presentation plan exceeds its cache bound.");
        var temporary = PlanPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            var output = new FileStream(temporary, options);
            await using (output.ConfigureAwait(false))
            {
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, PlanPath, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static void RequirePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).LinkTarget is not null) throw new InvalidDataException("Gateway material requires an absolute private path.");
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null) throw new InvalidDataException("Gateway material cannot traverse symbolic links.");
    }

    public void Dispose() => _lock.Dispose();
}
