namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeHttp3AltSvcFacts
{
    public static void Validate(int maxAgeSeconds)
    {
        if (maxAgeSeconds is < 0 or > 31536000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAgeSeconds));
        }
    }
}
