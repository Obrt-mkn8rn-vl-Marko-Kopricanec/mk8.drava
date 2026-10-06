namespace Mk8.Drava.Application.INF.Proxy.Http1;
internal static class Http1HeadTerminator
{
    public static int FindLength(ReadOnlySpan<byte> bytes)
    {
        for (var index = 3; index < bytes.Length; index++)
        {
            if (bytes[index - 3] == (byte)'\r' && bytes[index - 2] == (byte)'\n' && bytes[index - 1] == (byte)'\r' && bytes[index] == (byte)'\n')
            {
                return index + 1;
            }
        }

        return -1;
    }
}
