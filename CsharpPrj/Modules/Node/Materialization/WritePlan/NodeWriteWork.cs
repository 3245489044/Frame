namespace Modules.Node.Materialization.WritePlan;

/// <summary>
/// 单个写步骤（纯数据：声明「目标状态」，多步展开由写计划承担）。
/// <para>
/// <see cref="Medium"/> 与 <see cref="Collection"/> 一律为**字符串**：介质词（sqlite/graph/config）
/// 属宿主，进框架即破 K1/R9；框架只按二者的组合分派到注入的介质处理器。
/// </para>
/// </summary>
/// <param name="Id">计划内唯一 Id（依赖/短路引用键）。</param>
/// <param name="Medium">写介质（宿主词表，框架不解释）。</param>
/// <param name="Collection">介质内目标集合。</param>
/// <param name="Key">主键（可空：集合级操作）。</param>
/// <param name="PayloadJson">载荷 JSON（形态由介质处理器解释）。</param>
/// <param name="DependsOn">前序依赖 Id 集合（拓扑序）；null = 无依赖。</param>
/// <param name="LlmFree">本步骤是否 LLM-free（装配期断言输入）。</param>
public sealed record NodeWriteWork(
    string Id,
    string Medium,
    string Collection,
    string? Key,
    string PayloadJson,
    IReadOnlyList<string>? DependsOn = null,
    bool LlmFree = true)
{
    /// <summary>依赖集（去 null，空集非 null）。</summary>
    public IReadOnlyList<string> Dependencies => DependsOn ?? Array.Empty<string>();
}
