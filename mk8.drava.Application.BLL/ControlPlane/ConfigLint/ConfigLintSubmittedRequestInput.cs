using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ConfigLintSubmittedRequestInput
{
    public ConfigLintSubmittedRequestInput(string text, ProxyConfigurationNormalizeFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Submitted config format must be a defined format.");
        }

        Text = text;
        Format = format;
    }

    public string Text { get; }
    public ProxyConfigurationNormalizeFormat Format { get; }
}
