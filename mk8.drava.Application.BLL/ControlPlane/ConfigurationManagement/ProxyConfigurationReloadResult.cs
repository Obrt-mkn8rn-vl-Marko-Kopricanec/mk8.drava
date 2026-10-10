using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public abstract partial record ProxyConfigurationReloadResult<TProjection>
    where TProjection : class
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record ReloadedResult : ProxyConfigurationReloadResult<TProjection>
    {
        internal ReloadedResult(string sourceDirectory, DateTimeOffset attemptedAtUtc, int activeVersion, DateTimeOffset loadedAtUtc, ProxyConfigurationDiscovery discovery, ProxyListenerReloadResult listenerReload, TProjection activeConfiguration) : base(sourceDirectory, attemptedAtUtc, activeVersion, loadedAtUtc, loadedAtUtc, discovery, [], [])
        {
            if (listenerReload is not ProxyListenerReloadResult.AppliedResult)
            {
                throw new ArgumentException("A successful reload result requires a successful listener reload.", nameof(listenerReload));
            }

            ArgumentNullException.ThrowIfNull(activeConfiguration);
            ListenerReload = listenerReload;
            ActiveConfiguration = activeConfiguration;
        }

        public ProxyListenerReloadResult ListenerReload { get; }
        public TProjection ActiveConfiguration { get; }
    }

    private ProxyConfigurationReloadResult(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? loadedAtUtc, DateTimeOffset? lastSuccessfulLoadAtUtc, ProxyConfigurationDiscovery discovery, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileError> fileErrors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(fileErrors);
        ThrowIfNonPositive(activeVersion, nameof(activeVersion));
        SourceDirectory = sourceDirectory;
        AttemptedAtUtc = attemptedAtUtc;
        ActiveVersion = activeVersion;
        LoadedAtUtc = loadedAtUtc;
        LastSuccessfulLoadAtUtc = lastSuccessfulLoadAtUtc;
        Discovery = discovery;
        Errors = ConfigurationManagementList.Copy(errors);
        FileErrors = ConfigurationManagementList.Copy(fileErrors);
    }

    public string SourceDirectory { get; }
    public DateTimeOffset AttemptedAtUtc { get; }
    public int? ActiveVersion { get; }
    public DateTimeOffset? LoadedAtUtc { get; }
    public DateTimeOffset? LastSuccessfulLoadAtUtc { get; }
    public ProxyConfigurationDiscovery Discovery { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<ProxyConfigurationFileError> FileErrors { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "This member constructs or represents a case of the closed result union for its own configuration/projection type parameter, preserving typed absent/success/failure states and existing guards. It is not an unrelated static utility on a generic type.")]
    public static ProxyConfigurationReloadResult<TProjection> LoadFailed(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? loadedAtUtc, ProxyConfigurationDiscovery discovery, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileError> fileErrors, TProjection? activeConfiguration)
    {
        return new LoadFailedResult(sourceDirectory, attemptedAtUtc, activeVersion, loadedAtUtc, discovery, errors, fileErrors, activeConfiguration);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "This member constructs or represents a case of the closed result union for its own configuration/projection type parameter, preserving typed absent/success/failure states and existing guards. It is not an unrelated static utility on a generic type.")]
    public static ProxyConfigurationReloadResult<TProjection> ListenerReloadFailed(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? loadedAtUtc, ProxyConfigurationDiscovery discovery, ProxyListenerReloadResult listenerReload, TProjection? activeConfiguration)
    {
        ArgumentNullException.ThrowIfNull(listenerReload);
        if (listenerReload is not ProxyListenerReloadResult.FailedResult)
        {
            throw new ArgumentException("A listener reload failure result requires a failed listener reload.", nameof(listenerReload));
        }

        return new ListenerReloadFailedResult(sourceDirectory, attemptedAtUtc, activeVersion, loadedAtUtc, discovery, listenerReload, activeConfiguration);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "This member constructs or represents a case of the closed result union for its own configuration/projection type parameter, preserving typed absent/success/failure states and existing guards. It is not an unrelated static utility on a generic type.")]
    public static ProxyConfigurationReloadResult<TProjection> Reloaded(string sourceDirectory, DateTimeOffset attemptedAtUtc, int activeVersion, DateTimeOffset loadedAtUtc, ProxyConfigurationDiscovery discovery, ProxyListenerReloadResult listenerReload, TProjection activeConfiguration)
    {
        if (listenerReload is not ProxyListenerReloadResult.AppliedResult)
        {
            throw new ArgumentException("A successful reload result requires a successful listener reload.", nameof(listenerReload));
        }

        return new ReloadedResult(sourceDirectory, attemptedAtUtc, activeVersion, loadedAtUtc, discovery, listenerReload, activeConfiguration);
    }

    private static void ThrowIfNonPositive(int? value, string paramName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record LoadFailedResult : ProxyConfigurationReloadResult<TProjection>
    {
        internal LoadFailedResult(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? loadedAtUtc, ProxyConfigurationDiscovery discovery, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileError> fileErrors, TProjection? activeConfiguration) : base(sourceDirectory, attemptedAtUtc, activeVersion, loadedAtUtc, loadedAtUtc, discovery, errors, fileErrors)
        {
            ActiveConfiguration = activeConfiguration;
        }

        public TProjection? ActiveConfiguration { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record ListenerReloadFailedResult : ProxyConfigurationReloadResult<TProjection>
    {
        internal ListenerReloadFailedResult(string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? loadedAtUtc, ProxyConfigurationDiscovery discovery, ProxyListenerReloadResult listenerReload, TProjection? activeConfiguration) : base(sourceDirectory, attemptedAtUtc, activeVersion, loadedAtUtc, loadedAtUtc, discovery, listenerReload.Errors, listenerReload.Errors.Select(static error => ProxyConfigurationFileError.Global(error)).ToArray())
        {
            if (listenerReload is not ProxyListenerReloadResult.FailedResult)
            {
                throw new ArgumentException("A listener reload failure result requires a failed listener reload.", nameof(listenerReload));
            }

            ListenerReload = listenerReload;
            ActiveConfiguration = activeConfiguration;
        }

        public ProxyListenerReloadResult ListenerReload { get; }
        public TProjection? ActiveConfiguration { get; }
    }
}
