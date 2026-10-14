using Mk8.Drava.Application.BLL.NoConf;

namespace Mk8.Drava.Application.DAL.NoConf;

// Explicit legacy/offline adapter. Production composition supplies its protected role source.
public sealed class FileNoConfPolicySource : INoConfPolicySource
{
    private readonly string _path;

    public FileNoConfPolicySource(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Policy path must be absolute.", nameof(path));
        _path = path;
    }

    public async ValueTask<NoConfPolicy?> ReadAsync(CancellationToken cancellationToken) =>
        await NoConfPolicyFile.ReadAsync(_path, cancellationToken).ConfigureAwait(false);
}
