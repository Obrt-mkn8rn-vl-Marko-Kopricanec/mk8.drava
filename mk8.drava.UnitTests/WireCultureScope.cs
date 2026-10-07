using System.Globalization;

namespace Mk8.Drava.UnitTests;

internal sealed class WireCultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public WireCultureScope()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.PositiveSign = "P";
        culture.NumberFormat.NegativeSign = "M";
        CultureInfo.CurrentCulture = culture;
    }

    public void Dispose() => CultureInfo.CurrentCulture = _previous;
}
