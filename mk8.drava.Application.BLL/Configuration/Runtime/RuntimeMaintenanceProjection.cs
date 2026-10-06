namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeMaintenanceProjection
{
    public RuntimeMaintenanceProjection(bool Enabled, int? RetryAfterSeconds, string ContentType, string Body)
    {
        RuntimeGeneratedResponseFacts.ValidateMaintenance(RetryAfterSeconds, ContentType, Body);
        this.Enabled = Enabled;
        this.RetryAfterSeconds = RetryAfterSeconds;
        this.ContentType = ContentType;
        this.Body = Body;
    }

    public bool Enabled { get; }
    public int? RetryAfterSeconds { get; }
    public string ContentType { get; }
    public string Body { get; }
}
