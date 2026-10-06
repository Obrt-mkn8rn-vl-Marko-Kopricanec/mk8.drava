namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyLogPersistenceFailureStatus
{
    public ProxyLogPersistenceFailureStatus(DateTimeOffset TimestampUtc, string Category, string Reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(Reason);
        this.TimestampUtc = TimestampUtc;
        this.Category = Category;
        this.Reason = Reason;
    }

    public DateTimeOffset TimestampUtc { get; }
    public string Category { get; }
    public string Reason { get; }
}
