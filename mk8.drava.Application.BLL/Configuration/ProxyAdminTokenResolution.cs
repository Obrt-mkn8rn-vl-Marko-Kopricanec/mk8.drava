namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record ProxyAdminTokenResolution
{
    private ProxyAdminTokenResolution(string? token, string tokenEnvironmentVariable, string tokenSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenEnvironmentVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenSource);
        Token = token;
        TokenEnvironmentVariable = tokenEnvironmentVariable;
        TokenSource = tokenSource;
    }

    public string? Token { get; }
    public string TokenEnvironmentVariable { get; }
    public string TokenSource { get; }

    public static ProxyAdminTokenResolution Direct(string token, string tokenEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return new ProxyAdminTokenResolution(token: token, tokenEnvironmentVariable: tokenEnvironmentVariable, tokenSource: "direct");
    }

    public static ProxyAdminTokenResolution Environment(string token, string tokenEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return new ProxyAdminTokenResolution(token: token, tokenEnvironmentVariable: tokenEnvironmentVariable, tokenSource: "environment");
    }

    public static ProxyAdminTokenResolution None(string tokenEnvironmentVariable)
    {
        return new ProxyAdminTokenResolution(token: null, tokenEnvironmentVariable: tokenEnvironmentVariable, tokenSource: "none");
    }
}
