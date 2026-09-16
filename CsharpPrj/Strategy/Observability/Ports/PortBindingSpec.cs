using System.Text.Json;

namespace Strategy.Observability.Ports;

/// <summary>
/// 端口实参声明（E15 / 反射绑定的位置参数转换规则；位序即语义）。
/// <para>迁移说明：本类型为原测试侧 <c>Agent.TestShared.Bridge.PortArgSpec</c> 的**机制上移**（原样搬迁），
/// 作为「声明式绑定」的唯一模型载体（避免测试侧与内核两套同形类型）。</para>
/// </summary>
/// <param name="Convert">转换规则：<c>string</c> / <c>bool</c> / <c>int</c> / <c>double</c> / <c>csv</c>（CSV → List&lt;string&gt;）/ <c>json</c>（结构化直传，配 <paramref name="From"/>+<paramref name="Key"/>）/ <c>object</c>（位置实参对象直投：原样传实例，不文本化）。</param>
/// <param name="Optional">可省略（缺参 → <paramref name="Default"/> 或目标类型默认值）。</param>
/// <param name="NullLiteral">字面串 <c>null</c> 视为 null。</param>
/// <param name="Default">缺省值（可选槽位缺参时使用）。</param>
/// <param name="From">结构化取值来源（仅 <c>json</c> 用：<c>with</c> = 具名实参）。</param>
/// <param name="Key">结构化取值键名（仅 <c>json</c> 用）。</param>
public sealed record PortArgSpec(
    string Convert,
    bool Optional = false,
    bool NullLiteral = false,
    JsonElement? Default = null,
    string? From = null,
    string? Key = null);

/// <summary>
/// 端口绑定声明（E15 / G2）：交由 <see cref="ReflectiveBinding"/> 按 <see cref="Target"/>/<see cref="Method"/>/<see cref="Args"/>
/// 解析并调用。仅承载「机制可见」的绑定信息——调用方的驱动元数据（如旧 kind 名、产物字段）不属本模型。
/// </summary>
public sealed class PortBindingSpec
{
    public PortBindingSpec(string portKey, string target, string method, IReadOnlyList<PortArgSpec>? args = null)
    {
        PortKey = string.IsNullOrWhiteSpace(portKey)
            ? throw new ArgumentException("端口键不能为空", nameof(portKey))
            : portKey;
        Target = target ?? string.Empty;
        Method = method ?? string.Empty;
        Args = args ?? Array.Empty<PortArgSpec>();
    }

    /// <summary>端口键（<c>&lt;类型/接口&gt;.&lt;方法&gt;</c>）。</summary>
    public string PortKey { get; }

    /// <summary>目标类型全名（反射绑定用）。</summary>
    public string Target { get; }

    /// <summary>目标方法名（反射绑定用）。</summary>
    public string Method { get; }

    /// <summary>位置实参声明（位序即语义；空 = 无位置参数）。</summary>
    public IReadOnlyList<PortArgSpec> Args { get; }
}
