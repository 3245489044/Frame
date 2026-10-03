using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>任务供给流**空实现**（缩减切片注入）—— 有意不参与：不建容器、不归拢（NoOp，恒 <c>true</c> = 合法 no-op）。</summary>
public sealed class NoOpTaskProvisionFlow : ITaskProvisionFlow
{
    /// <summary>共享单例（无状态）。</summary>
    public static readonly NoOpTaskProvisionFlow Instance = new();

    private NoOpTaskProvisionFlow()
    {
    }

    /// <inheritdoc />
    public Task<bool> ProvisionAsync(string? historyId, string? historyName, string taskKey, CancellationToken ct = default)
        => Task.FromResult(true);
}
