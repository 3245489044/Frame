using DataCenter.Protocol.Standards;

namespace Strategy.Observability.Ports;

/// <summary>
/// 端口绑定声明（E15 / G2）：交由 <see cref="ReflectiveBinding"/> 按 <see cref="Target"/>/<see cref="Method"/>/<see cref="Args"/>
/// 解析并调用。仅承载「机制可见」的绑定信息——调用方的驱动元数据（如旧 kind 名、产物字段）不属本模型。
/// <para>
/// 实参声明形态 = 标准层 <see cref="PortArgStandard"/>（**单源**）：内核不再另立同形 <c>PortArgSpec</c>——
/// 该类型曾与标准模型字段逐个相同（<c>Convert</c>/<c>Optional</c>/<c>NullLiteral</c>/<c>Default</c>/<c>From</c>/<c>Key</c>），
/// 使「新增一个取值字段」需多处同步、漏一处即静默丢字段。取值声明视图经
/// <see cref="PortArgStandardExtensions.Options(PortArgStandard)"/> 投影到内核唯一转换表。
/// </para>
/// </summary>
public sealed class PortBindingSpec
{
    public PortBindingSpec(string portKey, string target, string method, IReadOnlyList<PortArgStandard>? args = null)
    {
        PortKey = string.IsNullOrWhiteSpace(portKey)
            ? throw new ArgumentException("端口键不能为空", nameof(portKey))
            : portKey;
        Target = target ?? string.Empty;
        Method = method ?? string.Empty;
        Args = args ?? Array.Empty<PortArgStandard>();
    }

    /// <summary>端口键（<c>&lt;类型/接口&gt;.&lt;方法&gt;</c>）。</summary>
    public string PortKey { get; }

    /// <summary>目标类型全名（反射绑定用）。</summary>
    public string Target { get; }

    /// <summary>目标方法名（反射绑定用）。</summary>
    public string Method { get; }

    /// <summary>位置实参声明（位序即语义；空 = 无位置参数）。</summary>
    public IReadOnlyList<PortArgStandard> Args { get; }
}
