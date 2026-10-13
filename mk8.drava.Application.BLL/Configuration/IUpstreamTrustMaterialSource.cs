namespace Mk8.Drava.Application.BLL.Configuration;

public interface IUpstreamTrustMaterialSource
{
    RuntimeTrustedRootCertificate Resolve(string materialId, string sha256);
}
