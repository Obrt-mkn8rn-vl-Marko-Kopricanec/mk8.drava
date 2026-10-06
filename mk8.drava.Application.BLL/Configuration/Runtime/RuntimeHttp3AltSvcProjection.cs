namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHttp3AltSvcProjection
{
    public RuntimeHttp3AltSvcProjection(bool Enabled, int MaxAgeSeconds)
    {
        RuntimeHttp3AltSvcFacts.Validate(MaxAgeSeconds);
        this.Enabled = Enabled;
        this.MaxAgeSeconds = MaxAgeSeconds;
    }

    public bool Enabled { get; }
    public int MaxAgeSeconds { get; }
}
