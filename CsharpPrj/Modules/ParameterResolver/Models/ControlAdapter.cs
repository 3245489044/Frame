
using System.Text.Json.Serialization;

namespace ParameterResolver.Models;

public class ControlAdapter
{
    [JsonPropertyName("outputAdapter")]
    public OutputAdapter? Output { get; set; }

    [JsonPropertyName("inputAdapter")]
    public InputAdapter? Input { get; set; }
}

public class OutputAdapter
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "text";

    [JsonPropertyName("clearBeforeWrite")]
    public bool ClearBeforeWrite { get; set; } = true;

    [JsonPropertyName("itemMethod")]
    public string? ItemMethod { get; set; }

    [JsonPropertyName("columns")]
    public List<string>? Columns { get; set; }

    [JsonPropertyName("function")]
    public string? Function { get; set; }

    [JsonPropertyName("functionParams")]
    public Dictionary<string, object>? FunctionParams { get; set; }
}

public class InputAdapter
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "text";

    [JsonPropertyName("valuePath")]
    public string? ValuePath { get; set; }

    [JsonPropertyName("function")]
    public string? Function { get; set; }
}
