namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
public interface IProxyRuntimeDirectoryProbe
{
    ProxyRuntimeDirectoryProbeResult Probe(string path, bool createIfMissing);
}
