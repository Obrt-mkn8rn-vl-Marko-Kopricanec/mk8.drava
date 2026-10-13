using Google.Protobuf;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Gateway.Hosting;

internal sealed class GatewayPlanCache : IDisposable
{
    private readonly FileStream _lock;
    private readonly PortableRole<GatewayBootstrap> _role;

    public GatewayPlanCache(PortableRole<GatewayBootstrap> role)
    {
        ArgumentNullException.ThrowIfNull(role);
        _role = role;
        var directory = role.Configuration.StateDirectory;
        RequirePath(directory);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(directory, "gateway.lock");
        RequirePath(path);
        _lock = GatewayWriterLease.Acquire(path);
    }

    public ValueTask<PresentationPlan?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var material = _role.ReadMaterial();
        if (material.Length == 0) return ValueTask.FromResult<PresentationPlan?>(null);
        var bytes = material.Reveal(128, 128 * 1024);
        try { return ValueTask.FromResult<PresentationPlan?>(PresentationPlan.Parser.ParseFrom(bytes)); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    public async ValueTask WriteAsync(PresentationPlan plan, CancellationToken cancellationToken)
    {
        var bytes = plan.ToByteArray();
        try
        {
            if (bytes.Length is < 128 or > 128 * 1024) throw new InvalidDataException("Presentation plan exceeds its cache bound.");
            await _role.WriteMaterialAsync(new SecretBytes(bytes), cancellationToken).ConfigureAwait(false);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void RequirePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).LinkTarget is not null) throw new InvalidDataException("Gateway material requires an absolute private path.");
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null) throw new InvalidDataException("Gateway material cannot traverse symbolic links.");
    }

    public void Dispose() => _lock.Dispose();
}
