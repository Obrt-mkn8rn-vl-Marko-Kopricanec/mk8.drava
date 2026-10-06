namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeStaticResponse
{
    public RuntimeStaticResponse(int StatusCode, string ContentType, string Body)
    {
        RuntimeGeneratedResponseFacts.ValidateStaticResponse(StatusCode, ContentType, Body);
        this.StatusCode = StatusCode;
        this.ContentType = ContentType;
        this.Body = Body;
    }

    public int StatusCode { get; }
    public string ContentType { get; }
    public string Body { get; }
}
