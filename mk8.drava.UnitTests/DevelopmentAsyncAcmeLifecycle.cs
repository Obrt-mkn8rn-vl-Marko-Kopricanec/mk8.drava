using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Configuration.Paths;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentAsyncAcmeLifecycle : IAcmeRenewalConfigurationSource, IAcmeCertificateIssuer, IAcmeCertificateMaterialWriter, IAcmeCertificateActivator, IProxyAcmeMetricsSink, IAcmeCertificateRenewalEventSink, IDisposable
{
    private readonly RegistryStateDirectory _directory = new();
    private readonly SemaphoreSlim _writerRelease = new(0, 1);
    private readonly SemaphoreSlim _activationRelease = new(0, 1);
    private readonly byte[] _pfx;
    private X509Certificate2? _material;
    public bool BlockWriter { get; init; }
    public bool BlockActivation { get; init; }
    public bool FailActivation { get; set; }
    public TaskCompletionSource WriterEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource WriterExited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ActivationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ActivationExited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Activated { get; private set; }
    public int Issued { get; private set; }
    public int Succeeded { get; private set; }
    public int Failed { get; private set; }
    public AcmeCertificateStatusStore Status { get; }
    public AcmeCertificateManager Manager { get; }
    public AcmeRenewalActiveCertificate? Previous { get; init; }
    public bool LifetimeAwareRenewal { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public bool IncludeWildcard { get; init; }
    public Action? BeforeIssue { get; init; }
    public Action? BeforeWrite { get; init; }
    public Action? BeforeActivation { get; init; }

    public DevelopmentAsyncAcmeLifecycle(IAcmeCertificateStatusPersistence? persistence = null, TimeProvider? clock = null)
    {
        Status = new AcmeCertificateStatusStore(persistence);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=development-lifecycle", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(90));
        _pfx = certificate.Export(X509ContentType.Pkcs12);
        Manager = new AcmeCertificateManager(this, this, new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = _directory.Path }), this, this, new AcmeChallengeStore(), Status, clock ?? TimeProvider.System, this, this);
    }

    public AcmeRenewalConfigurationInputReadResult ReadInput() => AcmeRenewalConfigurationInputReadResult.Available(new AcmeRenewalConfigurationInput(true, "acme", "https://development-ca.example/directory", ["ops@example.org"], true, 5,
        [new AcmeRenewalCertificateInput("site", true, IncludeWildcard ? ["site.example", "*.site.example"] : ["site.example"], 30, Previous, LifetimeAwareRenewal)], RetryAfter));
    public ValueTask<AcmeCertificateIssueResult> IssueAsync(AcmeCertificateIssueRequest request, AcmeChallengeStore challengeStore, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); Issued++; BeforeIssue?.Invoke(); return ValueTask.FromResult(AcmeCertificateIssueResult.Issued(_pfx)); }
    public void EnsureLayout(string dataDirectory, string storagePath) { }
    public RuntimeCertificate WriteAndLoad(AcmeCertificateMaterialWriteRequest request) => throw new InvalidOperationException("Manager must use the owned async writer.");

    public async ValueTask<RuntimeCertificate> WriteAndLoadAsync(AcmeCertificateMaterialWriteRequest request, CancellationToken cancellationToken)
    {
        WriterEntered.TrySetResult();
        try
        {
            if (BlockWriter) await _writerRelease.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            BeforeWrite?.Invoke();
            _material?.Dispose(); _material = X509CertificateLoader.LoadPkcs12(request.PfxBytes, null, X509KeyStorageFlags.EphemeralKeySet);
            return RuntimeCertificateFactory.Acme(request.CertificateId, _material, request.Domains);
        }
        finally { WriterExited.TrySetResult(); }
    }

    public void Activate(RuntimeCertificate certificate) => throw new InvalidOperationException("Manager must use the owned async activator.");
    public async ValueTask ActivateAsync(RuntimeCertificate certificate, CancellationToken cancellationToken)
    {
        ActivationEntered.TrySetResult();
        try
        {
            if (BlockActivation) await _activationRelease.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            BeforeActivation?.Invoke();
            if (FailActivation) throw new IOException("Development activation failed before publication.");
            Activated++;
        }
        finally { ActivationExited.TrySetResult(); }
    }

    public void ReleaseWriter() => _writerRelease.Release();
    public void ReleaseActivation() => _activationRelease.Release();
    public void AcmeRenewalAttempted() { }
    public void AcmeRenewalSucceeded() => Succeeded++;
    public void AcmeRenewalFailed() => Failed++;
    public void RenewalFailed(string certificateId, string? errorSummary) { }
    public void Dispose() { _material?.Dispose(); _writerRelease.Dispose(); _activationRelease.Dispose(); _directory.Dispose(); }
}
