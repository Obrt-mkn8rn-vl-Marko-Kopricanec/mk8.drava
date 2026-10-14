namespace Mk8.Drava.Application.BLL.Configuration;

public sealed class ProxyManualCertificateInput
{
    private readonly byte[] _pfx;
    public ProxyManualCertificateInput(string id, ReadOnlySpan<byte> pfx)
    {
        ProxyManualConfigurationInput.RequireName(id);
        if (pfx.Length is < 1 or > 1024 * 1024) throw new InvalidDataException("Protected manual PFX exceeds its material bound.");
        Id = id;
        _pfx = pfx.ToArray();
    }
    public string Id { get; }
    public byte[] CopyPfx() => (byte[])_pfx.Clone();
    public override string ToString() => "[redacted]";
}
