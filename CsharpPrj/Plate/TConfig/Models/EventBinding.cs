
namespace TConfig;

public class EventBinding
{
    public string? FunctionName { get; set; }

    public List<ParameterBinding>? Parameters { get; set; }

    public string? OutputTarget { get; set; }

    public string? OutputKey { get; set; }

    public string? Output { get; set; }

    public Dictionary<string, string>? OutputMapping { get; set; }

    public Dictionary<string, WorkflowDefinitionEntry>? Workflows { get; set; }
}

public class ParameterBinding
{
    public string? Name { get; set; }

    public string? Target { get => Name; set => Name = value; }

    public string? Source { get; set; }

    public string? Property { get; set; }

    public string? DefaultValue { get; set; }

    public object? Default { get => DefaultValue; set => DefaultValue = value?.ToString(); }

    public bool Required { get; set; }


    public string? SourceType { get; set; }

    /// <summary>
    /// 值语义声明（Text/Selection/Content/Checked/Range/Path，对应 <see cref="TConfig.Controls.ValueSemantic"/>）。
    /// 参数求值时按语义读控件值（如 Selection 读选中项 Tag）；缺省用控件类型契约的默认语义。
    /// </summary>
    public string? Semantic { get; set; }

    public string? Type { get; set; }

    public string? Converter { get; set; }

    public Dictionary<string, ParameterBinding>? Inputs { get; set; }

    public ConversionOptions? Options { get; set; }
}
