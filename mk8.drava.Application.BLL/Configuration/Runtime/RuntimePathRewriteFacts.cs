namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimePathRewriteFacts
{
    public static void Validate(string stripPrefix, string replacePrefix, string replacement)
    {
        ArgumentNullException.ThrowIfNull(stripPrefix);
        ArgumentNullException.ThrowIfNull(replacePrefix);
        ArgumentNullException.ThrowIfNull(replacement);
    }
}
