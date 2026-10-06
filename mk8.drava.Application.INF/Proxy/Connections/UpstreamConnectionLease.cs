namespace Mk8.Drava.Application.INF.Proxy.Connections;
public sealed class UpstreamConnectionLease : IAsyncDisposable
{
    private readonly UpstreamConnectionPool _pool;
    private bool _returned;
    internal UpstreamConnectionLease(UpstreamConnectionPool pool, PooledUpstreamConnection connection)
    {
        _pool = pool;
        Connection = connection;
    }

    internal PooledUpstreamConnection Connection { get; }
    public Stream Stream => Connection.Stream;

    public void MarkReusable()
    {
        Connection.MarkReusable();
    }

    public void MarkUnusable()
    {
        Connection.MarkUnusable();
    }

    public async ValueTask DisposeAsync()
    {
        if (_returned)
        {
            return;
        }

        _returned = true;
        try { if (Connection.Socket is null) await Connection.Stream.DisposeAsync().ConfigureAwait(false); }
        finally { _pool.Return(Connection); }
    }
}
