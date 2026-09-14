using System.Globalization;
using System.Text.Json;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 端口实参读取扩展（E15 / 原测试侧 Bridge\PortArgs.cs 上移）：把 <see cref="IPortInput"/> 的
/// 「位置实参文本 + 具名原始 JSON」读成调用方所需的强类型值，使绑定实现无需各写一份读参口径。
/// <para>
/// 纪律：缺失即抛（fail-fast，不静默取默认）；字面串 <c>null</c> 按既有约定视为 <c>null</c>（<see cref="ArgOrNull"/>）；
/// 结构化取值一律经 <c>with</c> 原始 JSON 反序列化（不手写编码解析）。
/// </para>
/// <para>
/// 迁移说明：本类为 <c>Agent.TestShared\Bridge\PortArgs.cs</c> 的**机制上移**（逐字搬语义），
/// 唯一差异 = 异常类型由测试侧 <c>SpecRunException</c> 换为内核 <see cref="PortInputMissingException"/> /
/// <see cref="PortInputTypeException"/>（内核不可引测试件），诊断前缀相应改为内核惯用的 <c>[port:input]</c>。
/// </para>
/// </summary>
public static class PortInputExtensions
{
    /// <summary>必需位置实参（文本）。缺失 → <see cref="PortInputMissingException"/>。</summary>
    public static string Arg(this IPortInput input, int index)
    {
        ArgumentNullException.ThrowIfNull(input);
        return index < input.Args.Count && input.Args[index] is { } value
            ? Text(value)
            : throw Missing(input, index);
    }

    /// <summary>可选位置实参；缺参或字面串 <c>null</c> → <c>null</c>。</summary>
    public static string? ArgOrNull(this IPortInput input, int index)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (index >= input.Args.Count || input.Args[index] is null) { return null; }

        var text = Text(input.Args[index]!);
        return string.Equals(text, "null", StringComparison.Ordinal) ? null : text;
    }

    /// <summary>布尔位置实参（<c>true/false</c>，兼容 <c>1/0</c>）。非法 → <see cref="PortInputTypeException"/>。</summary>
    public static bool ArgBool(this IPortInput input, int index)
    {
        var text = Arg(input, index);
        if (bool.TryParse(text, out var value)) { return value; }
        if (text is "1" or "0") { return text == "1"; }

        throw new PortInputTypeException(input.PortKey, $"第 {index} 个实参 '{text}' 不是布尔（true/false）");
    }

    /// <summary>整数位置实参（不变文化）。非法 → <see cref="PortInputTypeException"/>。</summary>
    public static int ArgInt(this IPortInput input, int index)
    {
        var text = Arg(input, index);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new PortInputTypeException(input.PortKey, $"第 {index} 个实参 '{text}' 不是整数");
    }

    /// <summary>具名实参（<c>with</c>）按目标类型反序列化；缺失即抛 <see cref="PortInputMissingException"/>。</summary>
    public static T With<T>(this IPortInput input, string key)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.With.TryGetValue(key, out var value) || value is null)
            throw new PortInputMissingException(input.PortKey, $"缺具名实参 '{key}'（with）");

        return value is JsonElement element
            ? JsonSerializer.Deserialize<T>(element.GetRawText())!
            : (T)value;
    }

    /// <summary>文本化（非字符串实参按不变量字符串形式取值）。</summary>
    private static string Text(object value)
        => value as string ?? value.ToString() ?? string.Empty;

    private static PortInputMissingException Missing(IPortInput input, int index)
        => new(input.PortKey, $"需要第 {index} 个位置实参，实际只有 {input.Args.Count} 个");
}
