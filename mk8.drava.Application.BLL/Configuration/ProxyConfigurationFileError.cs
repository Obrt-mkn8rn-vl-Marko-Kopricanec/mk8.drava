namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record ProxyConfigurationFileError
{
    private ProxyConfigurationFileError(string? path, string message)
    {
        if (path is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Path = path;
        Message = message;
    }

    public string? Path { get; }
    public string Message { get; }

    public static ProxyConfigurationFileError Global(string message)
    {
        return new ProxyConfigurationFileError(path: null, message: message);
    }

    public static ProxyConfigurationFileError ForPath(string path, string message)
    {
        return new ProxyConfigurationFileError(path: path, message: message);
    }
}
