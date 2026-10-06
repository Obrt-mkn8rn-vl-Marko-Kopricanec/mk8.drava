namespace Mk8.Drava.Application.INF.Proxy.Http1;
public sealed record Http1HeadReadResult
{
    private const int EmptyRequestHead = 0;
    private const int RequestHeadTooLarge = -1;
    private const int IncompleteRequestHead = -2;
    private const int UnreadableResponseHead = -1;
    private Http1HeadReadResult(int headLength, int totalBytesRead, ReadOnlyMemory<byte> headBytes, ReadOnlyMemory<byte> initialBodyBytes)
    {
        HeadLength = headLength;
        TotalBytesRead = totalBytesRead;
        HeadBytes = headBytes;
        InitialBodyBytes = initialBodyBytes;
    }

    public int HeadLength { get; }
    public int TotalBytesRead { get; }
    public ReadOnlyMemory<byte> HeadBytes { get; }
    public ReadOnlyMemory<byte> InitialBodyBytes { get; }
    public bool IsEmptyRequest => HeadLength == EmptyRequestHead;
    public bool IsRequestHeadTooLarge => HeadLength == RequestHeadTooLarge;
    public bool IsIncompleteRequest => HeadLength == IncompleteRequestHead;
    public bool HasReadableHead => HeadLength > 0;

    public static Http1HeadReadResult RequestEmpty()
    {
        return new Http1HeadReadResult(EmptyRequestHead, totalBytesRead: 0, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
    }

    public static Http1HeadReadResult RequestIncomplete(int totalBytesRead)
    {
        return CreateSentinel(IncompleteRequestHead, totalBytesRead);
    }

    public static Http1HeadReadResult RequestTooLarge(int totalBytesRead)
    {
        return CreateSentinel(RequestHeadTooLarge, totalBytesRead);
    }

    public static Http1HeadReadResult ResponseUnreadable(int totalBytesRead)
    {
        return CreateSentinel(UnreadableResponseHead, totalBytesRead);
    }

    public static Http1HeadReadResult Read(int headLength, int totalBytesRead, ReadOnlyMemory<byte> headBytes, ReadOnlyMemory<byte> initialBodyBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(headLength);
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytesRead);
        if (totalBytesRead < headLength)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytesRead), "Total bytes read cannot be smaller than the HTTP/1 head length.");
        }

        return new Http1HeadReadResult(headLength, totalBytesRead, headBytes, initialBodyBytes);
    }

    public static Http1HeadReadResult TranslatedRequestBody(ReadOnlyMemory<byte> initialBodyBytes)
    {
        return new Http1HeadReadResult(EmptyRequestHead, totalBytesRead: 0, ReadOnlyMemory<byte>.Empty, initialBodyBytes);
    }

    private static Http1HeadReadResult CreateSentinel(int headLength, int totalBytesRead)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytesRead);
        return new Http1HeadReadResult(headLength, totalBytesRead, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
    }
}
