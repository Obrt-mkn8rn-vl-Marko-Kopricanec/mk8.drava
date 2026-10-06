namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public static class ProxyCacheAgePolicy
{
    public static long CalculateAgeSeconds(DateTimeOffset storedAtUtc, DateTimeOffset nowUtc)
    {
        return Math.Max(0, (long)Math.Floor((nowUtc - storedAtUtc).TotalSeconds));
    }
}
