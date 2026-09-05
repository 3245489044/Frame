
using System.Globalization;

namespace ParameterResolver.Abstractions;

public class ConversionOptions
{
    public string? Format { get; set; }

    public string? CultureName { get; set; }

    public CultureInfo? Culture { get; set; }

    public Dictionary<string, object?>? Parameters { get; set; }

    public bool ThrowOnError { get; set; }

    public static readonly ConversionOptions Default = new();

    public CultureInfo GetEffectiveCulture()
    {
        if (Culture != null) return Culture;
        if (CultureName != null)
        {
            try { return CultureInfo.GetCultureInfo(CultureName); }
            catch { return CultureInfo.InvariantCulture; }
        }
        return CultureInfo.InvariantCulture;
    }
}
