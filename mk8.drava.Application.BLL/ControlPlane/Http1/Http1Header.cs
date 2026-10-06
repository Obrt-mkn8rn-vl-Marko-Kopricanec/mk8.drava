namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public readonly ref struct Http1Header
{
    public Http1Header(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        Name = name;
        Value = value;
    }

    public ReadOnlySpan<byte> Name { get; }
    public ReadOnlySpan<byte> Value { get; }
}
