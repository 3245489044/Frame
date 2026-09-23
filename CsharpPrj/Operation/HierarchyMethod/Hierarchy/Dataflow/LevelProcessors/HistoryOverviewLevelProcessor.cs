using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Processing;
using Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;
using Operation.HierarchyMethod.History.Runtime;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.LevelProcessors;

/// <summary>
/// 历史层处理器 —— 把「历史运行态概览」投影为层结论（读 <see cref="IHistoryRuntimeService"/>，LLM-free）。
/// <para>
/// 概览视图为只读投影（非独立状态源）；任一归拢任务失败终态即层失败，与子层结论合取后再向上传播。
/// </para>
/// <para>
/// 运行态（<c>runState.*</c>）为**派生**事实（<see cref="RunStateOrigin.Derived"/>）且**执行体恒为空**
/// （<see cref="ActorRef"/> = null）：容器层没有自己的执行体，其阶段由归拢实体汇总而来 ——
/// 这正是"容器与执行体解耦"的事实表达，故不存在"history 显示子智能体状态"的越层绑定。
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
        // 容器层的派生运行态：无执行体（Actor=null）、来源标记 Derived，阶段由归拢实体汇总。
        RunStatePayload.Write(payload, LevelRunState.Derived(node, DerivedStageOf(view),
            note: $"任务 {view.TaskCount}（运行 {view.RunningCount} / 挂起 {view.PausedCount} / 完成 {view.DoneCount} / 失败 {view.ErrorCount}）"));

        return view.ErrorCount == 0
            ? LevelProcessResult.Ok("overview", payload)
            : new LevelProcessResult(false, "fail", payload, $"历史 '{view.HistoryId}' 有 {view.ErrorCount} 个失败终态任务。");
    }

    /// <summary>容器派生阶段：有在途 → running；有失败 → failed；全部完成 → completed；其余 idle。</summary>
    private static string DerivedStageOf(HistoryRuntimeView view)
    {
        if (view.RunningCount > 0 || view.ActiveCount > 0) return RunStateStage.Running;
        if (view.ErrorCount > 0) return RunStateStage.Failed;
        if (view.TaskCount > 0 && view.DoneCount >= view.TaskCount) return RunStateStage.Completed;
        if (view.PausedCount > 0) return RunStateStage.Paused;
        return RunStateStage.Idle;
    }

    private static IReadOnlyDictionary<string, object?> Payload(string historyId)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["historyId"] = historyId,
            ["available"] = false,
            [RunStatePayload.AvailableKey] = false,
        };
}
