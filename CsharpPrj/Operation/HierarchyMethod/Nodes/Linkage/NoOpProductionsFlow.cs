using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>产出物化流**空实现**—— 有意不参与：不受理、不物化（返回 <see cref="FlowOutcome.NotApplicable"/>，静默）。</summary>
public sealed class NoOpProductionsFlow : IProductionsFlow
{
    /// <summary>共享单例（无状态）。</summary>
    public static readonly NoOpProductionsFlow Instance = new();

    private NoOpProductionsFlow()
    {
    }

    /// <inheritdoc />
    public Task<FlowOutcome> AcceptAndMaterializeAsync(ProductionsRequest request, CancellationToken ct = default)
        => Task.FromResult(FlowOutcome.NotApplicable("产出物化流空实现（有意不参与）"));
}
