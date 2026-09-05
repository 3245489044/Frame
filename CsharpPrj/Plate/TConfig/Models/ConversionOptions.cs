
namespace TConfig;

public class ConversionOptions
{
    public string? Format { get; set; }

    public string? CultureName { get; set; }

    public Dictionary<string, object?>? Parameters { get; set; }

    public bool ThrowOnError { get; set; }

    public static readonly ConversionOptions Default = new();
}
