using System.Security.Cryptography;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.DAL.Publication;

public sealed class GatewayMaterialStore(PortableRole<ApplicationBootstrap> role)
{
    private const int HeaderBytes = 36;
    private readonly PortableRole<ApplicationBootstrap> _role = role ?? throw new ArgumentNullException(nameof(role));
    public byte[]? Read()
    {
        var material = _role.ReadMaterial();
        if (material.Length == 0) return null;
        var bytes = material.Reveal(128 + HeaderBytes, (128 * 1024) + HeaderBytes);
        try
        {
            if (!bytes.AsSpan(0, 4).SequenceEqual("MK8M"u8)) throw new InvalidDataException("Unrecognized protected serving material envelope.");
            return bytes.AsSpan(HeaderBytes).ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public bool ConfiguredCertificateChanged(SecretBytes configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var material = _role.ReadMaterial();
        if (material.Length == 0) return false;
        var bytes = material.Reveal(128 + HeaderBytes, (128 * 1024) + HeaderBytes);
        byte[]? input = null;
        try
        {
            input = configured.Reveal(0, 65536);
            if (!bytes.AsSpan(0, 4).SequenceEqual("MK8M"u8)) throw new InvalidDataException("Unrecognized protected serving material envelope.");
            return !CryptographicOperations.FixedTimeEquals(bytes.AsSpan(4, 32), SHA256.HashData(input));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); if (input is not null) CryptographicOperations.ZeroMemory(input); }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> plan, SecretBytes configured, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configured);
        if (plan.Length is < 128 or > 128 * 1024) throw new InvalidDataException("Presentation plan exceeds its protected material bound.");
        var bytes = new byte[plan.Length + HeaderBytes];
        var input = configured.Reveal(0, 65536);
        try
        {
            "MK8M"u8.CopyTo(bytes);
            SHA256.HashData(input).CopyTo(bytes, 4);
            plan.Span.CopyTo(bytes.AsSpan(HeaderBytes));
            await _role.WriteMaterialAsync(new SecretBytes(bytes), cancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); if (input is not null) CryptographicOperations.ZeroMemory(input); }
    }
}
