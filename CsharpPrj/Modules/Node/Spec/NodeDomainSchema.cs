namespace Modules.Node.Spec;

/// <summary>一条**写门目标**：位置 id + 该位置的写入者（唯一写门 <c>NodeMaterializer</c> 的准入参数）。</summary>
/// <param name="PlacementId">位置 id（声明面）。</param>
/// <param name="WriterId">写入者 id（须等于该位置声明的 <c>writer</c>）。</param>
public sealed record NodeWriteTarget(string PlacementId, string WriterId);

/// <summary>
/// 节点**域描述基底**（通用机制；领域数据由上层派生记录承载）—— 把「同一节点机制在不同域的差异」
/// 收敛为一行参数：域键 / 写门目标 / 序数权威注入与否 / 运行态面 / 声明面位置 / 槽键。
/// <para>
/// 设计纪律（SingleImplementation / RedundancyElimination）：域的**装配逻辑只有一份**
/// （<see cref="Modules.Node.NodeFrameworkAssembly"/>），各域只提供数据；上层（Agent）
/// 以派生记录追加载体工厂与可见化事件名，本基底保持零领域词汇、零上层依赖。
/// </para>
/// </summary>
/// <param name="DomainKey">域键。</param>
/// <param name="WriteTargets">经**唯一写门**物化的位置→写入者。</param>
/// <param name="InjectSequenceAuthority">是否注入 <c>ISequenceAuthority</c>（仅序数需按轮重置的域）。</param>
/// <param name="RuntimeBound">是否有运行态内存面（内存面 = 读侧 overlay 源；非位置载体）。</param>
/// <param name="SubPathOverride">声明路径显式覆盖（默认由 <see cref="SubPathOf"/> 派生）。</param>
/// <param name="SlotKeyOverride">槽键显式覆盖（默认由 <see cref="SlotKeyOf"/> 派生）。</param>
public record NodeDomainSchema(
    string DomainKey,
    IReadOnlyList<NodeWriteTarget> WriteTargets,
    bool InjectSequenceAuthority,
    bool RuntimeBound = false,
    string? SubPathOverride = null,
    string? SlotKeyOverride = null)
{
    /// <summary>声明文件相对路径：默认派生（<see cref="SubPathOf"/>），复数名域经 override 给出。</summary>
    public string SubPath => SubPathOverride ?? SubPathOf(DomainKey);

    /// <summary>框架实例槽键：默认派生（<see cref="SlotKeyOf"/>）；override 保留为回退面。</summary>
    public string SlotKey => SlotKeyOverride ?? SlotKeyOf(DomainKey);

    /// <summary>声明路径派生默认（纯函数；形如 <c>nodes\{domainKey}.ushar</c>）。</summary>
    public static string SubPathOf(string domainKey) => $@"nodes\{domainKey}.ushar";

    /// <summary>槽键派生默认（纯函数；唯一形态 = <c>node-{domainKey}-framework</c>）。</summary>
    public static string SlotKeyOf(string domainKey) => $"node-{domainKey}-framework";

    /// <summary>
    /// 域描述登记的**唯一写入者**（单次物化须唯一写入者）—— 唯一判据与唯一文案：装配侧（违规即抛）
    /// 与读侧（违规即空 + 日志）共用本方法，消除「同一不变式两种失败语义」。
    /// </summary>
    /// <param name="writerId">唯一写入者 id（未满足唯一性时为空串）。</param>
    /// <param name="violation">违规原因（满足唯一性时为 <c>null</c>）。</param>
    public bool TryResolveSingleWriter(out string writerId, out string? violation)
    {
        var writers = WriteTargets
            .Select(t => t.WriterId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (writers.Length == 1)
        {
            writerId = writers[0];
            violation = null;
            return true;
        }

        writerId = string.Empty;
        violation = $"域 '{DomainKey}' 的写门目标含 {writers.Length} 个写入者（[{string.Join(",", writers)}]）："
                    + "单次物化须唯一写入者（同域多写者须各自建服务，禁合并成一次物化）";
        return false;
    }
}

/// <summary>
/// 节点域**注册表**（通用查询机制）：按域键取描述；未注册 ⇒ <see cref="Require{T}"/> 抛
/// （域清单是装配期事实，缺失即缺陷，不给静默兜底）。
/// </summary>
public static class NodeDomainRegistry
{
    /// <summary>按域键取描述（未注册 / 空键 ⇒ <c>false</c>，可见化由调用方负责）。</summary>
    public static bool TryGet<T>(IReadOnlyList<T> domains, string? domainKey, out T? descriptor)
        where T : NodeDomainSchema
    {
        descriptor = null;
        if (string.IsNullOrWhiteSpace(domainKey))
        {
            return false;
        }

        foreach (var candidate in domains)
        {
            if (string.Equals(candidate.DomainKey, domainKey, StringComparison.Ordinal))
            {
                descriptor = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>按域键取描述；未注册 ⇒ 抛（装配期显式失败，不给静默兜底）。</summary>
    public static T Require<T>(IReadOnlyList<T> domains, string domainKey)
        where T : NodeDomainSchema
        => TryGet(domains, domainKey, out var descriptor)
            ? descriptor!
            : throw new ArgumentException(
                $"未注册的节点域 '{domainKey}'（域清单唯一权威 = 上层域目录）", nameof(domainKey));
}
