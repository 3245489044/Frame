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
/// 任务层处理器 —— 把「任务运行状态」投影为层结论（读 <see cref="IHistoryRuntimePort"/> + 运行态门面
/// <see cref="IRunStateProvider"/>，LLM-free）。
/// <para>
/// 状态文本由注入端口填充（框架零 Agent 枚举依赖）；终态失败文本（Faulted/Cancelled/Error）判为层失败。
/// 未装配端口 / 未命中任务 = 成功 + available=false（诊断透传，不误报失败）。
/// </para>
/// <para>
/// 运行态（<c>runState.*</c>）为**层无关通用载荷**：阶段 / 执行体 / 轮次 / 在途时长 / 事实来源全部来自
/// <see cref="LevelRunState"/>（多来源经门面合并），本处理器不做"任务=子智能体"之类绑定假设
/// （执行体只是运行态的一个维度值）。
/// </para>
/// </summary>
public sealed class TaskStatusLevelProcessor : ILevelProcessor
{
    private static readonly HashSet<string> FailedStatusTexts =
        new(StringComparer.OrdinalIgnoreCase) { "Faulted", "Cancelled", "Error", "Failed" };

    private readonly IHistoryRuntimePort? _runtime;
    private readonly IRunStateProvider? _runStates;

    /// <param name="runtime">单实体运行状态读取端口（可空；未装配 → 透传诊断）。</param>
    /// <param name="runStates">运行态门面（可空；未装配 → 载荷仅含 available=false）。</param>
    public TaskStatusLevelProcessor(IHistoryRuntimePort? runtime = null, IRunStateProvider? runStates = null)
    {
        _runtime = runtime;
        _runStates = runStates;
    }

    /// <inheritdoc />
    public string ProcessId => HierarchySchemas.TaskProcessId;

    /// <inheritdoc />
    public async Task<LevelProcessResult> ProcessAsync(
        LevelRef node, ILevelProcessContext context, CancellationToken ct = default)
    {
        if (!HierarchyLevelRefs.IsTask(node)) return LevelProcessResult.Passed;
        if (_runtime is null) return LevelProcessResult.Ok("unassembled", Payload(taskKey: node.Id, available: false));

        var status = await _runtime.GetStatusAsync(node.Id, ct).ConfigureAwait(false);
        if (status is null) return LevelProcessResult.Ok("not-found", Payload(taskKey: node.Id, available: false));

        var failed = IsFailed(status.Status) || IsFailed(status.ExecutorStatus);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["taskKey"] = status.TaskKey,
            ["available"] = true,
            ["executorId"] = status.ExecutorId,
            ["status"] = status.Status,
            ["executorStatus"] = status.ExecutorStatus,
            ["round"] = status.Round,
            ["active"] = status.Active,
            ["decision"] = status.Decision,
        };
        // 通用运行态载荷（层无关）：来源合并（Live/Snapshot/Derived）与字段语义皆归运行态门面。
        var runState = _runStates is null ? null : await _runStates.GetAsync(node, ct).ConfigureAwait(false);
        RunStatePayload.Write(payload, runState);

        return failed
            ? new LevelProcessResult(false, "fail", payload, $"任务 '{status.TaskKey}' 处于失败终态（status={status.Status}, executor={status.ExecutorStatus}）。")
            : LevelProcessResult.Ok("pass", payload);
    }

    private static bool IsFailed(string? statusText)
        => !string.IsNullOrWhiteSpace(statusText) && FailedStatusTexts.Contains(statusText);

    private static IReadOnlyDictionary<string, object?> Payload(string taskKey, bool available)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["taskKey"] = taskKey,
            ["available"] = available,
            [RunStatePayload.AvailableKey] = false,
        };
}
