namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeCertificateMaterialWriteRequest
{
    private readonly byte[] _pfxBytes;
    public AcmeCertificateMaterialWriteRequest(string StoragePath, string CertificateId, IReadOnlyList<string> Domains, string DataDirectory, DateTimeOffset WrittenAtUtc, byte[] PfxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(StoragePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(CertificateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DataDirectory);
        this.StoragePath = StoragePath;
        this.CertificateId = CertificateId;
        this.Domains = AcmeCommandFacts.CopyRequiredStrings(Domains, nameof(Domains));
        this.DataDirectory = DataDirectory;
        this.WrittenAtUtc = WrittenAtUtc;
        _pfxBytes = AcmeCommandFacts.CopyRequiredBytes(PfxBytes, nameof(PfxBytes));
    }

    public string StoragePath { get; }
    public string CertificateId { get; }
    public IReadOnlyList<string> Domains { get; }
    public string DataDirectory { get; }
    public DateTimeOffset WrittenAtUtc { get; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819", Justification = "This getter returns a fresh caller-owned byte-array copy and never exposes its private backing storage. The imported cache/PFX byte-consumer API intentionally retains copy-on-read isolation.")]
    public byte[] PfxBytes => _pfxBytes.ToArray();
}
