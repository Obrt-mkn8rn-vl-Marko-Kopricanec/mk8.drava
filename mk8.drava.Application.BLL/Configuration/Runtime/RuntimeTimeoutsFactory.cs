namespace Mk8.Drava.Application.BLL.Configuration;
public static class RuntimeTimeoutsFactory
{
    public static RuntimeTimeouts ForHealthCheck(TimeSpan timeout)
    {
        return new RuntimeTimeouts(timeout, timeout, timeout, timeout, timeout, timeout, timeout, timeout, timeout, timeout);
    }
}
