namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHttp3AltSvcOptions
{
    public RuntimeHttp3AltSvcOptions(bool Enabled, int MaxAgeSeconds)
    {
        RuntimeHttp3AltSvcFacts.Validate(MaxAgeSeconds);
        this.Enabled = Enabled;
        this.MaxAgeSeconds = MaxAgeSeconds;
    }

    public bool Enabled { get; }
    public int MaxAgeSeconds { get; }
    public static RuntimeHttp3AltSvcOptions Disabled { get; } = new(false, 86400);
}
