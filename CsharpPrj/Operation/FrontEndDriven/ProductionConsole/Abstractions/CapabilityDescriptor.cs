using System.Globalization;
using System.Text.Json;

namespace FrontEndDriven.ProductionConsole;

/// <summary>参数控件形态（声明式表单的唯一词表；插件只给语义，渲染由控制台统一完成）。</summary>
public enum ParamKind
{
    Text,
    Multiline,
    Number,
    Bool,
    Enum,
    Range,
    Path,
    Json,
}

/// <summary>
/// 单个能力参数声明：键、控件形态、默认值与约束。纯数据，插件侧只声明、不渲染。
/// </summary>
public sealed class ParamSpec
{
    /// <summary>参数键（与工作流输入/节点参数同名）。</summary>
    public string Key { get; set; } = "";

    /// <summary>控件形态。</summary>
    public ParamKind Kind { get; set; } = ParamKind.Text;

    /// <summary>UI 显示名。</summary>
    public string Label { get; set; } = "";

    /// <summary>是否必填。</summary>
    public bool Required { get; set; }

    /// <summary>默认值（未声明 = 无默认，表单留空）。</summary>
    public object? Default { get; set; }

    /// <summary>数值下界（Kind=Number/Range）。</summary>
    public double? Min { get; set; }

    /// <summary>数值上界。</summary>
    public double? Max { get; set; }

    /// <summary>步进（Range 滑杆必填建议值）。</summary>
    public double? Step { get; set; }

    /// <summary>枚举候选（Kind=Enum）。</summary>
    public List<string>? Options { get; set; }

    /// <summary>单位后缀（如 s / bpm）。</summary>
    public string? Unit { get; set; }

    /// <summary>是否折叠到「高级」区。</summary>
    public bool Advanced { get; set; }

    /// <summary>行内说明（tooltip/提示）。</summary>
    public string? Help { get; set; }
}

/// <summary>能力（一行生产任务的可选操作对象）描述：参数 schema + 产物类型 + 动作。</summary>
public sealed class CapabilityDescriptor
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>分组（如 image / music）；同组任务串行执行。</summary>
    public string Group { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>
    /// 执行目标（插件解释的不透明字段，如工作流 Id / 模型名）。框架不解析，只透传给 Runner。
    /// </summary>
    public string? Target { get; set; }

    public List<ParamSpec> Params { get; set; } = new();

    /// <summary>产物类型（image/audio/text/json/file），供结果区选择渲染方式。</summary>
    public List<string> ArtifactKinds { get; set; } = new();

    /// <summary>可选动作 Id（如 clearCache / replayPlan），由插件命令处理器解释。</summary>
    public List<string> Actions { get; set; } = new();
}

/// <summary>
/// 参数集合工具：默认值合并、白名单过滤、类型归一与校验。
/// 渲染层与执行层共用同一套规则（表单值与 Runner 入参不会分叉）。
/// </summary>
public static class ParamSet
{
    /// <summary>取声明默认值（仅含非 null 默认），值已按 Kind 归一。</summary>
    public static Dictionary<string, object?> Defaults(IEnumerable<ParamSpec> specs)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            if (spec.Default is null) continue;
            result[spec.Key] = Coerce(spec.Kind, spec.Default);
        }
        return result;
    }

    /// <summary>合并：默认值 → 用户值（仅保留声明键，未知键丢弃）；结果已归一。</summary>
    public static Dictionary<string, object?> Merge(
        IEnumerable<ParamSpec> specs,
        IReadOnlyDictionary<string, object?>? values)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            var raw = values is not null && values.TryGetValue(spec.Key, out var v) ? v : spec.Default;
            if (raw is null) continue;
            result[spec.Key] = Coerce(spec.Kind, raw);
        }
        return result;
    }

    /// <summary>校验必填/数值范围/枚举候选；返回人类可读错误列表（空 = 通过）。</summary>
    public static IReadOnlyList<string> Validate(
        IEnumerable<ParamSpec> specs,
        IReadOnlyDictionary<string, object?> values)
    {
        var errors = new List<string>();
        foreach (var spec in specs)
        {
            var has = values.TryGetValue(spec.Key, out var raw) && raw is not null;
            var text = has ? Convert.ToString(raw, CultureInfo.InvariantCulture) : null;

            if (spec.Required && !has)
            {
                errors.Add($"缺少必填参数: {Label(spec)}");
                continue;
            }
            if (!has) continue;

            switch (spec.Kind)
            {
                case ParamKind.Number:
                case ParamKind.Range:
                    if (!TryNumber(text, out var number))
                    {
                        errors.Add($"参数 {Label(spec)} 不是有效数字");
                        break;
                    }
                    if (spec.Min is { } min && number < min) errors.Add($"参数 {Label(spec)} 小于下限 {min}");
                    if (spec.Max is { } max && number > max) errors.Add($"参数 {Label(spec)} 大于上限 {max}");
                    break;

                case ParamKind.Enum:
                    if (spec.Options is { Count: > 0 } && !spec.Options.Contains(text ?? "", StringComparer.OrdinalIgnoreCase))
                        errors.Add($"参数 {Label(spec)} 取值不在候选集: {text}");
                    break;

                case ParamKind.Bool:
                    if (!bool.TryParse(text, out _)) errors.Add($"参数 {Label(spec)} 不是布尔值");
                    break;

                case ParamKind.Text:
                case ParamKind.Multiline:
                case ParamKind.Path:
                case ParamKind.Json:
                    if (spec.Required && string.IsNullOrWhiteSpace(text))
                        errors.Add($"缺少必填参数: {Label(spec)}");
                    break;
            }
        }
        return errors;
    }

    /// <summary>按控件形态归一值（JsonElement → 原生；数字 → double；布尔 → bool；其余 → string）。</summary>
    public static object? Coerce(ParamKind kind, object? raw)
    {
        if (raw is null) return null;
        if (raw is JsonElement je) raw = FromJson(je);

        return kind switch
        {
            ParamKind.Bool => raw switch
            {
                bool b => b,
                string s when bool.TryParse(s, out var parsed) => parsed,
                _ => raw,
            },
            ParamKind.Number or ParamKind.Range => raw switch
            {
                double d => d,
                float f => (double)f,
                decimal m => (double)m,
                int i => (double)i,
                long l => (double)l,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d2) => d2,
                _ => raw,
            },
            ParamKind.Json => raw switch
            {
                JsonElement je2 => je2.GetRawText(),
                string s => s,
                _ => JsonSerializer.Serialize(raw),
            },
            _ => raw switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                _ => Convert.ToString(raw, CultureInfo.InvariantCulture),
            },
        };
    }

    /// <summary>显示名（无 Label 时回退 Key）。</summary>
    public static string Label(ParamSpec spec) =>
        string.IsNullOrWhiteSpace(spec.Label) ? spec.Key : spec.Label;

    private static bool TryNumber(string? text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static object? FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Object or JsonValueKind.Array => e.GetRawText(),
        _ => e.GetRawText(),
    };
}
