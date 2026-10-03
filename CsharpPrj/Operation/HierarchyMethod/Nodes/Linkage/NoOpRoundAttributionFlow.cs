using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>轮末沉淀流**空实现**—— 有意不参与：不建归属边、不入史（返回 <see cref="FlowOutcome.NotApplicable"/>，静默）。</summary>
public sealed class NoOpRoundAttributionFlow : IRoundAttributionFlow
{
    /// <summary>共享单例（无状态）。</summary>
    public static readonly NoOpRoundAttributionFlow Instance = new();

    private NoOpRoundAttributionFlow()
    {
    }

    /// <inheritdoc />
    public Task<FlowOutcome> SettleRoundAsync(RoundAttributionRequest request, CancellationToken ct = default)
        => Task.FromResult(FlowOutcome.NotApplicable("轮末沉淀流空实现（有意不参与）"));
}
