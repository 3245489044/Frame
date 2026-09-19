using System.Text.Json;
using Strategy.Observability.Diagnostics;
using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口输入读参小件（P3 退场族共用）：把内核 <see cref="IPortInput"/> 的「位置实参文本 + 具名原始 JSON」
/// 读成绑定实现所需的取值口径（缺失即抛 / 可空位返回 null / 非字符串按不变量文本），
/// 使逐族搬迁的实现只在读参上零语义漂移。
/// <para>位置实参读法沿用内核 <see cref="PortInputExtensions"/>（Arg/ArgOrNull/ArgBool/ArgInt）。</para>
/// <para>
/// 归位批 I2（宿主上移）：本件从测试侧搬入执行面，异常类型由测试侧 <c>SpecRunException</c> 换为内核
/// <see cref="PortInputMissingException"/>（内核不可引测试件）；<c>SpecJson.Options</c>（测试侧用例文件口径）
/// 换为执行面自有的 <see cref="Json"/>（同名设置）；<c>SpecTables</c> / <c>SpecLiterals</c> 的转发入口
/// （Rows / Presence / MapEach / EnumOf / EnumOr）随「L0 机制只留测试侧单源」退场——其单源仍在
/// <c>TestInfrastructure.DataDriven</c>，本件不含第二份解析口径。
/// </para>
/// </summary>
public static class SpecPortInput
{
    /// <summary>
    /// 结构化取值反序列化选项（执行面自有单源）：大小写不敏感 + 跳过注释 + 允许尾逗号。
    /// 内核 <see cref="PortInputExtensions.With{T}"/> 用缺省（大小写敏感）选项，结构化 DTO 的 camelCase
    /// 键会静默漏绑，故端口侧统一走本入口。
    /// </summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>是否提供某具名实参。</summary>
    public static bool HasWith(IPortInput input, string key) => input.With.ContainsKey(key);

    /// <summary>取具名实参原始 JSON；缺失（或非 JSON 载体）→ fail-fast（列出现有键，防拼写漂移静默通过）。</summary>
    public static JsonElement WithElement(IPortInput input, string key)
    {
        if (input.With.TryGetValue(key, out var value) && value is JsonElement element) return element;

        var keys = input.With.Count == 0 ? "(无)" : string.Join(", ", input.With.Keys);
        throw new PortInputMissingException(input.PortKey, $"缺少具名实参 '{key}'（现有：{keys}）");
    }

    /// <summary>取具名实参为文本（字符串原样；对象/数组取 JSON 原文）；缺失且有 fallback → fallback。</summary>
    public static string WithString(IPortInput input, string key, string? fallback = null)
    {
        if (input.With.TryGetValue(key, out var value)) return Text(value, key);

        return fallback ?? Text(WithElement(input, key), key);
    }

    /// <summary>取具名实参为文本；缺失或 JSON null → null。</summary>
    public static string? WithStringOrNull(IPortInput input, string key)
        => input.With.TryGetValue(key, out var value) ? NullableText(value, key) : null;

    /// <summary>取具名实参为整数（缺失 / 非数字 → fallback）。</summary>
    public static int WithInt(IPortInput input, string key, int fallback)
        => input.With.TryGetValue(key, out var value) && value is JsonElement element && element.TryGetInt32(out var parsed)
            ? parsed
            : value is int number ? number : fallback;

    /// <summary>取具名实参为布尔（缺失 / 非布尔 → fallback）。</summary>
    public static bool WithBool(IPortInput input, string key, bool fallback)
        => input.With.TryGetValue(key, out var value) && value is JsonElement element
            ? element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback,
            }
            : value is bool flag ? flag : fallback;

    /// <summary>位置实参的文本形式（逐个取，缺失即抛）。</summary>
    public static IReadOnlyList<string> ArgsText(IPortInput input)
        => input.Args.Select((_, index) => input.Arg(index)).ToArray();

    /// <summary>取具名实参为文本数组（字符串单值视为单元素集；非字符串元素取 JSON 原文）。</summary>
    public static IReadOnlyList<string> TextList(IPortInput input, string key)
    {
        var element = WithElement(input, key);
        if (element.ValueKind == JsonValueKind.String)
            return new[] { element.GetString() ?? string.Empty };
        if (element.ValueKind != JsonValueKind.Array)
            throw new PortInputMissingException(input.PortKey, $"具名实参 '{key}' 须为字符串数组");

        return element.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.GetRawText())
            .ToArray();
    }

    /// <summary>
    /// 具名实参按目标类型反序列化（口径同 L0 用例面：大小写不敏感 + 跳过注释 + 允许尾逗号；
    /// 缺失 / null 载体即抛）。
    /// </summary>
    public static T With<T>(IPortInput input, string key)
        => JsonSerializer.Deserialize<T>(WithElement(input, key).GetRawText(), Json)
           is { } value
            ? value
            : throw new PortInputMissingException(input.PortKey, $"具名实参 '{key}' 反序列化为 null");

    /// <summary>可选字符串数组具名实参（未声明 → 空集合；数组 / 单串均可）。</summary>
    public static IReadOnlyList<string> OptionalStringList(IPortInput input, string key)
        => HasWith(input, key) ? TextList(input, key) : Array.Empty<string>();

    /// <summary>空槽位保留型文本数组（JSON null / undefined → null 元素；数组 / 单串均可，缺键 → 空集合）。
    /// 供需区分「null 名」与「缺键」的端口探针（如注册表 null 名登记）使用。</summary>
    public static IReadOnlyList<string?> WithNullableTextList(IPortInput input, string key)
    {
        if (!HasWith(input, key))
            return Array.Empty<string?>();

        var element = WithElement(input, key);
        if (element.ValueKind == JsonValueKind.String)
            return new[] { element.GetString() };

        return element.EnumerateArray().Select(item => item.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => (string?)null,
            JsonValueKind.String => item.GetString(),
            _ => item.GetRawText(),
        }).ToArray();
    }

    /// <summary>
    /// 按**首个**分隔符拆两段（两侧去空白；无分隔符 → 第二段取 <paramref name="fallbackSecond"/>）。
    /// 端口面「编码串 → 分段」基元单源（<c>key=value</c> / <c>id|payload</c> 等形态共用）。
    /// </summary>
    public static (string First, string Second) Pair(string text, char separator, string fallbackSecond = "")
    {
        var index = text.IndexOf(separator);
        return index < 0
            ? (text.Trim(), fallbackSecond)
            : (text[..index].Trim(), text[(index + 1)..].Trim());
    }

    /// <summary>按 <c>|</c> 拆字段数组（**保留空段**，逐段去空白）——供「字段数不足」显式报错的编码串使用。</summary>
    public static string[] Fields(string text)
        => text.Split('|').Select(field => field.Trim()).ToArray();

    /// <summary>整数解析（失败 / 非数字 → <paramref name="fallback"/>）——与 L0 同形口径的端口侧入口。</summary>
    public static int IntOr(string text, int fallback)
        => int.TryParse(text, out var value) ? value : fallback;

    /// <summary>位置实参文本（越界 → 空串；不抛）——供「可选尾随实参」的声明式端口使用。</summary>
    public static string ArgOrEmpty(IPortInput input, int index)
        => index >= 0 && index < input.Args.Count ? input.Arg(index) : string.Empty;

    /// <summary>
    /// 可空布尔具名实参（缺键 / JSON null / 非布尔 → <c>null</c>）——与 <see cref="WithBool"/> 的
    /// 「fallback 缺省值」口径**互补**：本入口保留「未声明」与「声明为 false」的区别。
    /// </summary>
    public static bool? WithBoolOrNull(IPortInput input, string key)
        => input.With.TryGetValue(key, out var value) && value is JsonElement element
            ? element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    /// <summary>
    /// 可选对象具名实参按目标类型反序列化（缺键 / JSON null → <c>null</c>）；口径同 <see cref="With{T}"/>。
    /// </summary>
    public static T? WithOrNull<T>(IPortInput input, string key) where T : class
        => WithOrNull<T>(input, key, Json);

    /// <summary>
    /// 可选对象具名实参按目标类型反序列化（带**显式 wire 选项**）：用于须保真既有序列化口径的场景
    /// （如产品 Api 契约的 camelCase 命名策略），避免换选项造成 DTO 静默漏绑。
    /// </summary>
    public static T? WithOrNull<T>(IPortInput input, string key, JsonSerializerOptions options) where T : class
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.With.TryGetValue(key, out var value)
            || value is not JsonElement element
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return JsonSerializer.Deserialize<T>(element.GetRawText(), options);
    }

    /// <summary>JSON 文本 → <see cref="JsonElement"/>（空白 / 非法即 fail-fast，不静默返回 default）。</summary>
    public static JsonElement WithJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new PortInputMissingException(string.Empty, "需要 JSON 文本（实际为空）");

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>对象字段读取（键**忽略大小写**；非字符串取 JSON 原文；非对象 / 缺键 → <c>null</c>）。</summary>
    public static string? FieldOf(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.GetRawText();
        }

        return null;
    }

    /// <summary>对象现有字段名清单（诊断用：报「缺少字段」时列出实际键，防拼写漂移静默通过）。</summary>
    public static IReadOnlyList<string> FieldNames(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject().Select(property => property.Name).ToArray()
            : Array.Empty<string>();

    private static string Text(object? value, string key)
        => value switch
        {
            null => throw new PortInputMissingException(string.Empty, $"具名实参 '{key}' 为 null（需要文本）"),
            JsonElement element => element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.GetRawText(),
            string text => text,
            _ => value.ToString() ?? string.Empty,
        };

    private static string? NullableText(object? value, string key)
        => value switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            _ => Text(value, key),
        };
}

