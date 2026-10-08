namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public static class Http1ChunkSizeParser
{
    public static bool TryParseLine(ReadOnlySpan<byte> lineWithCrlf, out long chunkSize)
    {
        chunkSize = 0;
        if (lineWithCrlf.Length < 3 || lineWithCrlf[^2] != (byte)'\r' || lineWithCrlf[^1] != (byte)'\n')
        {
            return false;
        }

        var line = lineWithCrlf[..^2];
        var semicolon = line.IndexOf((byte)';');
        var sizeBytes = semicolon >= 0 ? line[..semicolon] : line;
        if (sizeBytes.Length == 0)
        {
            return false;
        }

        foreach (ref readonly var value in sizeBytes)
        {
            var digit = HexValue(value);
            if (digit < 0)
            {
                return false;
            }

            if (chunkSize > (long.MaxValue - digit) / 16)
            {
                return false;
            }

            chunkSize = chunkSize * 16 + digit;
        }

        return true;
    }

    private static int HexValue(byte value)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            return value - (byte)'0';
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            return value - (byte)'a' + 10;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            return value - (byte)'A' + 10;
        }

        return -1;
    }
}
