using Mk8.Drava.Application.DAL.Acme;

namespace Mk8.Drava.Application.DAL.Publication;

public static class GatewayMaterialStore
{
    public static byte[]? Read(string stateDirectory)
    {
        var path = Path.Combine(stateDirectory, "gateway-serving.plan");
        if (new FileInfo(path).LinkTarget is not null) throw new InvalidDataException("Serving plan cannot be a symbolic link.");
        return File.Exists(path) ? PrivateCertificateFile.ReadProtected(path, 128, 128 * 1024) : null;
    }

    public static ValueTask WriteAsync(string stateDirectory, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        PrivateCertificateFile.WriteProtectedAsync(Path.Combine(stateDirectory, "gateway-serving.plan"), bytes, overwrite: true, 128, 128 * 1024, cancellationToken);
}
