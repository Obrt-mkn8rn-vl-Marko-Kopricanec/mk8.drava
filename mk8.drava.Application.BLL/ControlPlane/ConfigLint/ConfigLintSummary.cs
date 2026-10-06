namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ConfigLintSummary
{
    public ConfigLintSummary(int Info, int Warning, int Error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Info);
        ArgumentOutOfRangeException.ThrowIfNegative(Warning);
        ArgumentOutOfRangeException.ThrowIfNegative(Error);
        this.Info = Info;
        this.Warning = Warning;
        this.Error = Error;
    }

    public static ConfigLintSummary Empty { get; } = new(0, 0, 0);
    public int Info { get; }
    public int Warning { get; }
    public int Error { get; }
}
