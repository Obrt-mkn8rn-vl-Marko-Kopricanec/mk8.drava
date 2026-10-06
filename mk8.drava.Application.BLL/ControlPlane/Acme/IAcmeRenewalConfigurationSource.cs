using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public interface IAcmeRenewalConfigurationSource
{
    AcmeRenewalConfigurationInputReadResult ReadInput();
}
