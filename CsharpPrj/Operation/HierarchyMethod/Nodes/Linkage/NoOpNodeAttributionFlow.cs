using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 归属边流**空实现**（缩减 / 读投影切片注入）—— 有意不参与：不写边、不发诊断（NoOp）。
/// <para>
/// 与「未装配（真缺失）」分离（<see cref="FlowOutcome.NotApplicable"/>）：缩减切片因防递归 / 读投影不反写而
/// **本就不应**写归属边，注入本实现使调用点恒非空、零判空，且不再逐次误报 <c>flow_entry_missing</c>。
/// </para>
/// </summary>
public sealed class NoOpNodeAttributionFlow : INodeAttributionFlow
{
    /// <summary>共享单例（无状态）。</summary>
    public static readonly NoOpNodeAttributionFlow Instance = new();

    private NoOpNodeAttributionFlow()
    {
    }

    /// <inheritdoc />
    public Task AttachAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task SessionAttachAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default)
        => Task.CompletedTask;
}
