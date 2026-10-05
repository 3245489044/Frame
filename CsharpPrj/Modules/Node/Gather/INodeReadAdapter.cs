using Modules.Node.Projection;

namespace Modules.Node.Gather;

/// <summary>
/// 读侧**适配**产物（第一段输出）：三态结果 + **单一化**（保序、去重、滤空）后的读项序列。
/// </summary>
/// <param name="Outcome">读面三态。</param>
/// <param name="Items">单一化读项（<see cref="NodeReadNormalizer.Normalize"/> 产物）。</param>
public sealed record NodeGatherRead(NodeGatherOutcome Outcome, IReadOnlyList<NodeProjectionItem> Items);

/// <summary>
/// **适配端口**（第一段）：把「多个读来源（统一域端口 / 直读回退 / 内存叠加）」收敛为
/// **单一读事实面** —— 治「读源碎片化 + 键口径漂移」。
/// <para>
/// 框架只定义契约；实现由业务侧注入（由它认域读端口与回退，框架**零领域知识**）。
/// 单一来源语义：可用来源优先 → 不可用才回退 → 仍无 ⇒
/// <see cref="NodeGatherOutcome.Unavailable"/>（不伪造）；<c>OkButEmpty</c> 不清面。
/// </para>
/// </summary>
public interface INodeReadAdapter
{
    /// <summary>读取某容器（任务键 / 会话键）的单一化读项序列。</summary>
    /// <param name="container">容器（身份键首段）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<NodeGatherRead> ReadAsync(string container, CancellationToken cancellationToken = default);
}

/// <summary>
/// 读项**单一化**（适配段的通用归一；框架级、零领域）：滤 <c>null</c>、滤空键、按 <c>Key</c> 去重、保序。
/// 业务适配器在拼装多来源后统一经本器收口，使「去重 / 去空 / 保序」不各写一套。
/// </summary>
public static class NodeReadNormalizer
{
    /// <summary>归一（不修改输入；返回新序列）。</summary>
    public static IReadOnlyList<NodeProjectionItem> Normalize(IEnumerable<NodeProjectionItem?>? items)
    {
        if (items is null)
        {
            return Array.Empty<NodeProjectionItem>();
        }

        var result = new List<NodeProjectionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            if (!seen.Add(item.Key))
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }
}
