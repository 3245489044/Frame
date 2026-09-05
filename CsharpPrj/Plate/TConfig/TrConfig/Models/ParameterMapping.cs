
namespace TConfig;

public class ParameterMapping
{
    public string SourceType { get; set; } = "value";

    public string Source { get; set; } = "";

    public string Target { get; set; } = "";

    public string? Property { get; set; }

    public string? DefaultValue { get; set; }
}
