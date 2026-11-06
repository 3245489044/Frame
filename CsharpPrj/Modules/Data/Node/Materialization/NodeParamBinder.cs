using Modules.Node.Identity;

namespace Modules.Node.Materialization;

/// <summary>单条参数字绑定（裸端口键 + 节点限定键 + 原生值）。</summary>
/// <param name="Port">端口名。</param>
/// <param name="BareKey">裸端口键（供下游按 <c>{port}</c> 取用）。</param>
/// <param name="QualifiedKey">节点限定键（<c>{nodeKey}.{port}</c>；节点键空 ⇒ 空串）。</param>
/// <param name="Value">端口值（非 null）。</param>
public sealed record NodePortBindEntry(
    string Port,
    string BareKey,
    string QualifiedKey,
    object Value);

/// <summary>
/// 节点输出端口 → 参数字的通用绑定器：只做「键构造 + 空值过滤」，写入动作由调用方执行。
/// <para>
/// 口径：null 端口值不产绑定（与框架侧「空端口不合入」一致）；裸键恒为端口名；
/// 限定键经 <see cref="NodePortKeys.Format"/> 单源构造（禁调用点手拼 <c>"."</c>）。
/// </para>
/// </summary>
public static class NodeParamBinder
{
    /// <summary>绑定端口集（输入序即输出序；null/空白端口跳过）。</summary>
    /// <param name="nodeKey">节点键（可空；空 ⇒ 不产限定键）。</param>
    /// <param name="ports">端口 → 值。</param>
    public static IReadOnlyList<NodePortBindEntry> Bind(
        string? nodeKey,
        IEnumerable<KeyValuePair<string, object?>> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var result = new List<NodePortBindEntry>();
        foreach (var pair in ports)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            result.Add(new NodePortBindEntry(
                pair.Key,
                pair.Key,
                NodePortKeys.Format(nodeKey, pair.Key),
                pair.Value));
        }

        return result;
    }
}
