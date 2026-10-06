namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyConfigurationReloadResponse
{
    public ProxyConfigurationReloadResponse(bool succeeded, string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? loadedAtUtc, DateTimeOffset? lastSuccessfulLoadAtUtc, ProxyConfigurationDiscoveryResponse discovery, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileErrorResponse> fileErrors, ProxyConfigurationResponse? activeConfiguration, ProxyListenerReloadResponse? listenerReload)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        Succeeded = succeeded;
        SourceDirectory = sourceDirectory;
        AttemptedAtUtc = attemptedAtUtc;
        ActiveVersion = activeVersion;
        LoadedAtUtc = loadedAtUtc;
        LastSuccessfulLoadAtUtc = lastSuccessfulLoadAtUtc;
        Discovery = discovery;
        Errors = ApiResponseList.Copy(errors);
        FileErrors = ApiResponseList.Copy(fileErrors);
        ActiveConfiguration = activeConfiguration;
        ListenerReload = listenerReload;
    }

    public bool Succeeded { get; }
    public string SourceDirectory { get; }
    public DateTimeOffset AttemptedAtUtc { get; }
    public int? ActiveVersion { get; }
    public DateTimeOffset? LoadedAtUtc { get; }
    public DateTimeOffset? LastSuccessfulLoadAtUtc { get; }
    public ProxyConfigurationDiscoveryResponse Discovery { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<ProxyConfigurationFileErrorResponse> FileErrors { get; }
    public ProxyConfigurationResponse? ActiveConfiguration { get; }
    public ProxyListenerReloadResponse? ListenerReload { get; }
}
