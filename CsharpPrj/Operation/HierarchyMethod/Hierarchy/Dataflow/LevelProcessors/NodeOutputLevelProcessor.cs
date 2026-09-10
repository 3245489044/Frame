using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Processing;
using Operation.HierarchyMethod.Nodes;
using Operation.WorkFlowMethod.Nodes.Contracts;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.LevelProcessors;

/// <summary>
/// 节点层处理器 —— 把「节点输出端口」投影为层结论（读 <see cref="INodeOutputStore"/>，LLM-free）。
/// <para>
/// 无输出 = 合法空结果（首轮未产出），记为成功且载荷 available=false，**不判失败**
/// （预算截断 / 业务失败由引擎与其它层各自承载，避免把「还没产出」误报为失败）。
/// </para>
/// </summary>
public sealed class NodeOutputLevelProcessor : ILevelProcessor
{
    private readonly INodeOutputStore? _outputs;
    private readonly string _workflowId;

    /// <param name="outputs">节点输出存储端口（可空；未装配 → 透传诊断，不阻断确定性路径）。</param>
    /// <param name="workflowId">节点层默认工作流命名空间（节点引用未带作用域时回退）。</param>
    public NodeOutputLevelProcessor(INodeOutputStore? outputs = null, string? workflowId = null)
    {
        _outputs = outputs;
        _workflowId = NodeOutputKeys.Normalize(workflowId);
    }

    /// <inheritdoc />
    public string ProcessId => HierarchySchemas.NodeProcessId;

    /// <inheritdoc />
    public async Task<LevelProcessResult> ProcessAsync(
        LevelRef node, ILevelProcessContext context, CancellationToken ct = default)
    {
        // 处理器可经计划覆盖落到其它层：层不匹配即透传，杜绝「按节点键查任务」之类的错位。
        if (!HierarchyLevelRefs.IsNode(node)) return LevelProcessResult.Passed;
        if (_outputs is null) return LevelProcessResult.Ok("unassembled", Payload(false, 0));

        var workflowId = string.IsNullOrWhiteSpace(node.Scope) ? _workflowId : node.Scope!;
        var output = await _outputs.TryGetNodeOutputAsync(workflowId, node.Id, ct).ConfigureAwait(false);
        if (output is null) return LevelProcessResult.Ok("empty", Payload(true, 0));

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = true,
            ["ports"] = output.OutputPorts.Count,
            ["visibility"] = output.Visibility.ToString(),
            ["digest"] = output.OutputDigest,
            ["valueScore"] = output.ValueScore,
        };
        return LevelProcessResult.Ok("output", payload);
    }

    private static IReadOnlyDictionary<string, object?> Payload(bool available, int ports)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = available,
            ["ports"] = ports,
        };
}
