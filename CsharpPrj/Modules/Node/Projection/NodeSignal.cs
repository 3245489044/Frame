namespace Modules.Node.Projection;

/// <summary>
/// 节点信号元数据（选择/排名用）：<b>字段名与默认值来自声明</b>（框架不硬编码 module/region 等领域字段）。
/// <para>
/// 值语义 = 文本袋；数值比较由选择服务按可解析数值优先处理，缺失字段排在末位（不伪造 0）。
/// </para>
/// </summary>
/// <param name="Values">信号字段袋（字段名 → 文本值）。</param>
public sealed record NodeSignal(IReadOnlyDictionary<string, string> Values)
{
    /// <summary>空信号（无任何字段）。</summary>
    public static NodeSignal Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>取字段值（缺席 ⇒ null）。</summary>
    public string? Get(string field)
        => Values.TryGetValue(field, out var value) ? value : null;

    /// <summary>是否含任何字段。</summary>
    public bool IsEmpty => Values.Count == 0;
}
