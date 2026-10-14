namespace Mk8.Drava.Application.BLL.Configuration;

public sealed class ProxyManualSiteInput
{
    public ProxyManualSiteInput(string name, ProxyManualSiteFormat format, string document)
    {
        ProxyManualConfigurationInput.RequireName(name);
        ArgumentNullException.ThrowIfNull(document);
        if (!Enum.IsDefined(format)) throw new InvalidDataException("Protected manual site format is unsupported.");
        Name = name;
        Format = format;
        Document = document;
    }
    public string Name { get; }
    public ProxyManualSiteFormat Format { get; }
    public string Document { get; }
    public override string ToString() => "[redacted]";
}
