using System.Net.Security;
using System.Runtime.CompilerServices;

namespace Mk8.Drava.Application.INF.Proxy.Connections;

internal static class TlsReadBoundary
{
    private static readonly ConditionalWeakTable<SslStream, TlsRecordReadStream> Readers = new();

    public static SslStream Create(Stream inner, RemoteCertificateValidationCallback validate)
    {
        var reader = new TlsRecordReadStream(inner);
        SslStream? tls = null;
        try
        {
            tls = new SslStream(reader, leaveInnerStreamOpen: false, validate);
            Readers.Add(tls, reader);
            return tls;
        }
        catch
        {
            if (tls is null) reader.Dispose();
            else tls.Dispose();
            throw;
        }
    }

    public static bool IsAtRecordBoundary(Stream stream) => stream is not SslStream tls
        || Readers.TryGetValue(tls, out var reader) && !reader.HasPartialRecord;
}

