using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Processing;
using Operation.HierarchyMethod.History.Runtime;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.LevelProcessors;

/// <summary>
/// 历史层处理器 —— 把「历史运行态概览」投影为层结论（读 <see cref="IHistoryRuntimeService"/>，LLM-free）。
/// <para>
/// 概览视图为只读投影（非独立状态源）；任一归拢任务失败终态即层失败，与子层结论合取后再向上传播。
/// </para>
/// </summary>
public sealed class HistoryOverviewLevelProcessor : ILevelProcessor
{
    private readonly IHistoryRuntimeService? _runtime;

    /// <param name="runtime">history 会话编排门面（可空；未装配 → 透传诊断）。</param>
    public HistoryOverviewLevelProcessor(IHistoryRuntimeService? runtime = null) => _runtime = runtime;

    /// <inheritdoc />
    public string ProcessId => HierarchySchemas.HistoryProcessId;

    /// <inheritdoc />
    public async Task<LevelProcessResult> ProcessAsync(
        LevelRef node, ILevelProcessContext context, CancellationToken ct = default)
    {
        if (!HierarchyLevelRefs.IsHistory(node)) return LevelProcessResult.Passed;
        if (_runtime is null) return LevelProcessResult.Ok("unassembled", Payload(node.Id));

        var view = await _runtime.GetOverviewAsync(node.Id, ct).ConfigureAwait(false);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["historyId"] = view.HistoryId,
            ["taskCount"] = view.TaskCount,
            ["active"] = view.ActiveCount,
            ["running"] = view.RunningCount,
            ["paused"] = view.PausedCount,
            ["done"] = view.DoneCount,
            ["error"] = view.ErrorCount,
        };
        return view.ErrorCount == 0
            ? LevelProcessResult.Ok("overview", payload)
            : new LevelProcessResult(false, "fail", payload, $"历史 '{view.HistoryId}' 有 {view.ErrorCount} 个失败终态任务。");
    }

    private static IReadOnlyDictionary<string, object?> Payload(string historyId)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["historyId"] = historyId,
            ["available"] = false,
        };
}
