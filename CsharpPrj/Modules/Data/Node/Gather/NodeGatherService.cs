namespace Modules.Node.Gather;

/// <summary>
/// 域**聚拢服务**（通用编排；框架件）：读适配 → 聚拢引擎 → <see cref="NodeGatherResult{TGroupKey, TShape}"/>。
/// <para>
/// 一域一实例（域键注入，其余同构）；<b>零领域知识</b>：适配器与引擎由上层注入，
/// 输出为泛型结果（域 DTO 由上层映射）。呈现只是塑形的一种用途。
/// </para>
/// </summary>
/// <typeparam name="TGroupKey">分组键。</typeparam>
/// <typeparam name="TShape">目标形态。</typeparam>
public sealed class NodeGatherService<TGroupKey, TShape>
    where TGroupKey : notnull
{
    private readonly INodeReadAdapter _adapter;
    private readonly NodeGatherEngine<TGroupKey, TShape> _engine;

    /// <param name="domainKey">本服务读取的域键。</param>
    /// <param name="adapter">读适配器（单一化读事实面）。</param>
    /// <param name="engine">聚拢 + 塑形引擎。</param>
    public NodeGatherService(
        string domainKey,
        INodeReadAdapter adapter,
        NodeGatherEngine<TGroupKey, TShape> engine)
    {
        DomainKey = string.IsNullOrWhiteSpace(domainKey)
            ? throw new ArgumentException("域键不可为空", nameof(domainKey))
            : domainKey;
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>本服务读取的域键。</summary>
    public string DomainKey { get; }

    /// <summary>聚拢某容器：读适配 → 引擎构建（唯一编排）。</summary>
    /// <param name="container">容器（任务键 / 会话键）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<NodeGatherResult<TGroupKey, TShape>> GatherAsync(
        string container, CancellationToken cancellationToken = default)
    {
        var read = await _adapter.ReadAsync(container ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return _engine.Build(DomainKey, container ?? string.Empty, read.Items, read.Outcome);
    }
}
