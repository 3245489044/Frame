
namespace TConfig;

public class EventBinding
{
    public string? FunctionName { get; set; }

    public List<ParameterBinding>? Parameters { get; set; }

    public string? OutputTarget { get; set; }

    public string? OutputKey { get; set; }

    public string? Output { get; set; }

    /// <summary>输出写语义（见 <see cref="OutputWriteModes"/>）；null/空 = <see cref="OutputWriteModes.Replace"/>。</summary>
    public string? OutputMode { get; set; }

    public Dictionary<string, string>? OutputMapping { get; set; }

    public Dictionary<string, WorkflowDefinitionEntry>? Workflows { get; set; }
}

/// <summary>
/// 输出写语义（<c>bindings.outputMode</c>）：决定函数返回值如何落到 <c>bindings.output</c> 目标控件。
/// <see cref="Replace"/>（缺省）= 覆盖写（普通结果区）；<see cref="Append"/> = 追加一行（终端/日志型目标，
/// 使多次运行的结果累积可见，如 MainLayout 右侧 outputTextBox 终端）。
/// 消费方：EcsEventBus 输出回写（唯一裁决点，见 IEcsStateAccessor.AppendValue）。
/// </summary>
public static class OutputWriteModes
{
    /// <summary>覆盖写（缺省）：目标控件 Text = 本次函数返回值。</summary>
    public const string Replace = "replace";

    /// <summary>追加写：目标控件 Text = 现有内容 + 换行 + 本次函数返回值（终端日志语义）。</summary>
    public const string Append = "append";
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
