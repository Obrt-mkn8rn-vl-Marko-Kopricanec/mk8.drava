using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyCertificateValiditySource
{
    private string _id = string.Empty;
    public ProxyCertificateValiditySource(string Id, DateTime NotBefore, DateTime NotAfter)
    {
        this.Id = Id;
        this.NotBefore = NotBefore;
        this.NotAfter = NotAfter;
    }

    public string Id
    {
        get => _id;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _id = value;
        }
    }

    public DateTime NotBefore { get; init; }
    public DateTime NotAfter { get; init; }
}
