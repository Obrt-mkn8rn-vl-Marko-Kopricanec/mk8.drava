namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
public sealed class AdmissionLease : IDisposable
{
    private readonly Action _release;
    private int _disposed;
    public AdmissionLease(Action release)
    {
        _release = release;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _release();
        }
    }
}
