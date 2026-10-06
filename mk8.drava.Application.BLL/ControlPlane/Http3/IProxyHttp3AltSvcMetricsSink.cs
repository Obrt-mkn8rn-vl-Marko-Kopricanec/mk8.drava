namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public interface IProxyHttp3AltSvcMetricsSink
{
    void Http3AltSvcEmitted();
    void Http3AltSvcSuppressed();
}
