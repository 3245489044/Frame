using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 轮契约装饰器（兼容名）—— 实现已收敛至 <see cref="RoundLifecycleBackend"/>（dep: replaced-by(RoundLifecycleBackend)）。
/// 保留本类仅为装配点/测试零改动一迭代；<c>enabled=false</c> 仍为纯转发（应急回退）。
/// </summary>
public sealed class RoundContractBackend : IRoundExecutionBackend
{
    private readonly RoundLifecycleBackend _lifecycle;

    public RoundContractBackend(
        IRoundExecutionBackend inner,
        IRoundStatePort? statePort = null,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _lifecycle = new RoundLifecycleBackend(inner, statePort, enabled: enabled);
    }

    public Task<NodeRoundResult> RunRoundAsync(
        RoundSpec spec, NodeRoundContext ctx, CancellationToken ct = default)
        => _lifecycle.RunRoundAsync(spec, ctx, ct);
}

