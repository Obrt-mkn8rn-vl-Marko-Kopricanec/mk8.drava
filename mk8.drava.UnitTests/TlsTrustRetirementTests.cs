using System.Net;
using System.Net.Sockets;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class TlsTrustRetirementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedRetirementJoinsItsSocketOwnerBeforeReleasingCertificateTrustAsync(bool failure)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token).ConfigureAwait(true);
        using var peer = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(true);
        RetirementObserver? untransferredTrust = null;
        TlsRecordReadStream? reader = null;
        var inner = new HeldTransportRetirementStream(client.GetStream(), failure, deadline.Token);
        Task? first = null;
        Task? second = null;
        try
        {
            untransferredTrust = new RetirementObserver();
            var trust = untransferredTrust;
            reader = new TlsRecordReadStream(inner, untransferredTrust);
            untransferredTrust = null;
            first = reader.DisposeAsync().AsTask();
            await inner.Started.WaitAsync(deadline.Token).ConfigureAwait(true);
            second = reader.DisposeAsync().AsTask();
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(0, trust.DisposeCount);
            inner.Release();
            await JoinAsync(first, failure, deadline.Token).ConfigureAwait(true);
            await JoinAsync(second, failure, deadline.Token).ConfigureAwait(true);
            await JoinAsync(reader.DisposeAsync().AsTask(), failure, deadline.Token).ConfigureAwait(true);
            Assert.Equal(1, inner.DisposeCount);
            Assert.Equal(1, trust.DisposeCount);
            var bytes = new byte[1];
            Assert.Equal(0, await peer.GetStream().ReadAsync(bytes, deadline.Token).ConfigureAwait(true));
        }
        finally
        {
            inner.Release();
            try
            {
                if (first is not null) await JoinAsync(first, failure, deadline.Token).ConfigureAwait(true);
                if (second is not null) await JoinAsync(second, failure, deadline.Token).ConfigureAwait(true);
                if (reader is not null) await JoinAsync(reader.DisposeAsync().AsTask(), failure, deadline.Token).ConfigureAwait(true);
            }
            finally { untransferredTrust?.Dispose(); }
        }
    }

    private static async Task JoinAsync(Task task, bool failure, CancellationToken cancellationToken)
    {
        if (!failure) { await task.WaitAsync(cancellationToken).ConfigureAwait(true); return; }
        var exception = await Assert.ThrowsAsync<IOException>(() => task.WaitAsync(cancellationToken)).ConfigureAwait(true);
        Assert.Equal("Controlled transport retirement failure.", exception.Message);
    }

    private sealed class RetirementObserver : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
