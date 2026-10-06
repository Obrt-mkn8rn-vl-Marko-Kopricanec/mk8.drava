namespace Mk8.Drava.Application.BLL.Configuration;
public static class RuntimeHttp3EnablementExtensions
{
    public static string ToConfigText(this RuntimeHttp3Enablement enablement)
    {
        return enablement switch
        {
            RuntimeHttp3Enablement.Default => "default",
            _ => "disabled"
        };
    }
}
