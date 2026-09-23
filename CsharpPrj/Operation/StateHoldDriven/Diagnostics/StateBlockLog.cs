using System.Text.Json;
using Common.PlaceholderFramework.Integrated.Handlers;

namespace Operation.StateBlock.Diagnostics;

/// <summary>
/// StateHoldDriven 专用日志模块 —— 池运行时过程的关键步骤日志（spawn / 探活 / 预热 / 请求 / 超时归因）。
/// 走统一日志注册体系（<see cref="ModuleLog"/>）：消息模板注册在 stateblock_log_messages.json，
/// keyPrefix=stateblock:，由 framework.config.json → logging.modules 单一注册源驱动，无需独立宿主注入。
/// 统一 messageId：Step → StateBlock_Step，Fail → StateBlock_Fail；池运行时事件（预热/就绪清理/daemon stderr/trace/csg）
/// 各自注册 StateBlock_WarmupStart/Done/Failed/Hung、StateBlock_ReadyTimeoutCleanup、StateBlock_DaemonStderr/Trace/Csg；
/// data 序列化为 JSON 字符串。
/// </summary>
public static class StateBlockLog
{
    private static readonly Func<string, object?, bool> _log = ModuleLog.Create("StateBlock", "stateblock:");

    /// <summary>记录一个池运行时过程步（Info 语义）。</summary>
    public static void Step(string pool, string step, object? data = null)
        => _log("StateBlock_Step", new { pool, step, data = data is null ? "" : JsonSerializer.Serialize(data) });

    /// <summary>
    /// 记录一个池运行时细步（Debug 语义）——worker 级预热明细（每 worker 一次发送/一次完成），
    /// 终端（min level=Info）只保留 <see cref="PoolInit"/>/<see cref="PoolReady"/>/<see cref="WarmupSummary"/> 汇总，
    /// 失败仍经 <see cref="WarmupFailed"/>/<see cref="WarmupHung"/>/<see cref="Fail"/> 显性告警。
    /// </summary>
    public static void StepDebug(string pool, string step, object? data = null)
        => _log("StateBlock_StepDebug", new { pool, step, data = data is null ? "" : JsonSerializer.Serialize(data) });

    /// <summary>记录一个失败/挂起归因（Warning 语义，携带阶段与 stderr 尾部）。</summary>
    public static void Fail(string pool, string step, object? data = null)
        => _log("StateBlock_Fail", new { pool, step, data = data is null ? "" : JsonSerializer.Serialize(data) });

    /// <summary>记录预热失败（Warning，携带阶段与 stderr 尾部）。</summary>
    public static void WarmupFailed(string pool, string workerId, string? stage, string? stderrTail, string? error)
        => _log("StateBlock_WarmupFailed", new { pool, workerId, stage, stderrTail, error });

    /// <summary>记录预热挂起/超时归因（Warning，携带阶段、AEF 结束阶段与 stderr 尾部）。</summary>
    public static void WarmupHung(string pool, string workerId, string? stage, string? phase, string? stderrTail, string? error)
        => _log("StateBlock_WarmupHung", new { pool, workerId, stage, phase, stderrTail, error });

    /// <summary>记录就绪预算耗尽/取消后的未就绪 daemon 清理（Warning）。</summary>
    public static void ReadyTimeoutCleanup(string pool, int warm, object? workers)
        => _log("StateBlock_ReadyTimeoutCleanup", new { pool, warm, workers = workers is null ? "" : JsonSerializer.Serialize(workers) });

    /// <summary>记录 daemon stderr 一行（Info，定位卡点）。</summary>
    public static void DaemonStderr(string pool, string workerId, string? line)
        => _log("StateBlock_DaemonStderr", new { pool, workerId, line });

    /// <summary>记录 daemon 阶段追踪帧（Info）。</summary>
    public static void DaemonTrace(string pool, string workerId, string? stage, object? detail)
        => _log("StateBlock_DaemonTrace", new { pool, workerId, stage, detail = detail is null ? "" : JsonSerializer.Serialize(detail) });

    /// <summary>记录 daemon CSG 状态帧（Info，透传 CanonicalForm 契约载荷）。</summary>
    /// <remarks>form 已是 TS 侧序列化好的 CanonicalForm JSON 字符串（跨语言契约载荷），直接透传即可，
    /// 若再经 <see cref="JsonSerializer.Serialize(object?)"/> 会把整段 JSON 当字符串字面量二次转义（" → \u0022），造成不可读。</remarks>
    public static void DaemonCsg(string pool, string workerId, string? stage, string? form)
        => _log("StateBlock_DaemonCsg", new { pool, workerId, stage, form = form ?? "" });

    /// <summary>记录 CSG 状态迁移（首帧已透传完整 form，后续仅记新状态，避免整表刷屏）。</summary>
    public static void DaemonCsgTransition(string pool, string workerId, string? stage)
        => _log("StateBlock_DaemonCsgTransition", new { pool, workerId, stage });

    /// <summary>记录请求提交（Info，携带选中的 worker 与剩余预算）。</summary>
    public static void SendStart(string pool, string requestId, string workerId, string cmd, double remainingSec)
        => _log("StateBlock_SendStart", new { pool, requestId, workerId, cmd, remainingSec });

    /// <summary>记录请求成功完成（Info，携带耗时）。</summary>
    public static void SendDone(string pool, string requestId, string workerId, string cmd, double elapsedMs)
        => _log("StateBlock_SendDone", new { pool, requestId, workerId, cmd, elapsedMs });

    /// <summary>记录基建故障（挂起/被杀）换 worker 重试（Warning，携带最后阶段与 stderr 尾部，供归因引擎为何不推进）。</summary>
    public static void SendRetry(string pool, string requestId, string workerId, string cmd, int attempt, string? error, string? lastStage = null, string? stderrTail = null)
        => _log("StateBlock_SendRetry", new { pool, requestId, workerId, cmd, attempt, error, lastStage, stderrTail });

    /// <summary>记录请求业务失败（Warning，daemon 正常返回业务错误，不重试；携带最后阶段与 stderr 尾部）。</summary>
    public static void SendFailed(string pool, string requestId, string workerId, string cmd, string? error, string? lastStage = null, string? stderrTail = null)
        => _log("StateBlock_SendFailed", new { pool, requestId, workerId, cmd, error, lastStage, stderrTail });

    /// <summary>记录请求被取消（Warning）。</summary>
    public static void SendCancelled(string pool, string requestId, string workerId, string cmd, string? error)
        => _log("StateBlock_SendCancelled", new { pool, requestId, workerId, cmd, error });

    /// <summary>记录 worker 进程退出（被 kill/崩溃，Warning）。</summary>
    public static void WorkerExited(string pool, string workerId)
        => _log("StateBlock_WorkerExited", new { pool, workerId });

    /// <summary>记录补位/重启失败（Warning）。</summary>
    public static void RespawnFailed(string pool, string workerId, string? error)
        => _log("StateBlock_RespawnFailed", new { pool, workerId, error });

    /// <summary>记录非法 worker 状态迁移（Error，状态机一致性被破坏 = 代码 bug 信号）。</summary>
    public static void IllegalTransition(string pool, string workerId, string from, string to)
        => _log("StateBlock_IllegalTransition", new { pool, workerId, from, to });

    /// <summary>记录池就绪成功（Info，携带耗时）。</summary>
    public static void PoolReady(string pool, int readyCount, double elapsedSec)
        => _log("StateBlock_PoolReady", new { pool, readyCount, elapsedSec });

    /// <summary>记录背压等待开始（Warning，无可用就绪 worker 时）。</summary>
    public static void BackpressureWait(string pool, int readyCount)
        => _log("StateBlock_BackpressureWait", new { pool, readyCount });

    /// <summary>记录补位/重启尝试（Info）。</summary>
    public static void Respawn(string pool, string workerId)
        => _log("StateBlock_Respawn", new { pool, workerId });

    /// <summary>记录预热汇总（Info，成功数/总数）。</summary>
    public static void WarmupSummary(string pool, int warmed, int total)
        => _log("StateBlock_WarmupSummary", new { pool, warmed, total });

    /// <summary>记录池初始化参数（Info）。</summary>
    public static void PoolInit(string pool, int poolSize, double idleTimeoutSec, double warmupBudgetSec, int infraRetries)
        => _log("StateBlock_PoolInit", new { pool, poolSize, idleTimeoutSec, warmupBudgetSec, infraRetries });

    /// <summary>记录池销毁（Info）。</summary>
    public static void PoolDispose(string pool)
        => _log("StateBlock_PoolDispose", new { pool });
}

