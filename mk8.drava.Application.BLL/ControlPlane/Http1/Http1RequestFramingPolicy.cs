namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public static class Http1RequestFramingPolicy
{
    public static bool HasFramedBody(Http1RequestHead requestHead)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        return requestHead.Framing.Kind == Http1BodyKind.Chunked || (requestHead.Framing.Kind == Http1BodyKind.ContentLength && requestHead.Framing.ContentLength.GetValueOrDefault() > 0);
    }
}
