using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Processing;
using Operation.HierarchyMethod.History.Runtime;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.LevelProcessors;

/// <summary>
/// 任务层处理器 —— 把「任务运行状态」投影为层结论（读 <see cref="IHistoryRuntimePort"/>，LLM-free）。
/// <para>
/// 状态文本由注入端口填充（框架零 Agent 枚举依赖）；终态失败文本（Faulted/Cancelled/Error）判为层失败。
/// 未装配端口 / 未命中任务 = 成功 + available=false（诊断透传，不误报失败）。
/// </para>
/// </summary>
public sealed class TaskStatusLevelProcessor : ILevelProcessor
{
    private static readonly HashSet<string> FailedStatusTexts =
        new(StringComparer.OrdinalIgnoreCase) { "Faulted", "Cancelled", "Error", "Failed" };

    private readonly IHistoryRuntimePort? _runtime;

    /// <param name="runtime">单 task 运行状态读取端口（可空；未装配 → 透传诊断）。</param>
    public TaskStatusLevelProcessor(IHistoryRuntimePort? runtime = null) => _runtime = runtime;

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
        };
}
