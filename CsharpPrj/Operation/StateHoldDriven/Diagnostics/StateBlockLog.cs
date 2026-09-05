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
    public static void Step(string step, object? data = null)
        => _log("StateBlock_Step", new { step, data = data is null ? "" : JsonSerializer.Serialize(data) });

    /// <summary>记录一个失败/挂起归因（Warning 语义，携带阶段与 stderr 尾部）。</summary>
    public static void Fail(string step, object? data = null)
        => _log("StateBlock_Fail", new { step, data = data is null ? "" : JsonSerializer.Serialize(data) });

    /// <summary>记录预热失败（Warning，携带阶段与 stderr 尾部）。</summary>
    public static void WarmupFailed(string workerId, string? stage, string? stderrTail, string? error)
        => _log("StateBlock_WarmupFailed", new { workerId, stage, stderrTail, error });

    /// <summary>记录预热挂起/超时归因（Warning，携带阶段、AEF 结束阶段与 stderr 尾部）。</summary>
    public static void WarmupHung(string workerId, string? stage, string? phase, string? stderrTail, string? error)
        => _log("StateBlock_WarmupHung", new { workerId, stage, phase, stderrTail, error });

    /// <summary>记录就绪预算耗尽/取消后的未就绪 daemon 清理（Warning）。</summary>
    public static void ReadyTimeoutCleanup(int warm, object? workers)
        => _log("StateBlock_ReadyTimeoutCleanup", new { warm, workers = workers is null ? "" : JsonSerializer.Serialize(workers) });

    /// <summary>记录 daemon stderr 一行（Info，定位卡点）。</summary>
    public static void DaemonStderr(string workerId, string? line)
        => _log("StateBlock_DaemonStderr", new { workerId, line });

    /// <summary>记录 daemon 阶段追踪帧（Info）。</summary>
    public static void DaemonTrace(string workerId, string? stage, object? detail)
        => _log("StateBlock_DaemonTrace", new { workerId, stage, detail = detail is null ? "" : JsonSerializer.Serialize(detail) });

    /// <summary>记录 daemon CSG 状态帧（Info，透传 CanonicalForm 契约载荷）。</summary>
    /// <remarks>form 已是 TS 侧序列化好的 CanonicalForm JSON 字符串（跨语言契约载荷），直接透传即可，
    /// 若再经 <see cref="JsonSerializer.Serialize(object?)"/> 会把整段 JSON 当字符串字面量二次转义（" → \u0022），造成不可读。</remarks>
    public static void DaemonCsg(string workerId, string? stage, string? form)
        => _log("StateBlock_DaemonCsg", new { workerId, stage, form = form ?? "" });

    /// <summary>记录 CSG 状态迁移（首帧已透传完整 form，后续仅记新状态，避免整表刷屏）。</summary>
    public static void DaemonCsgTransition(string workerId, string? stage)
        => _log("StateBlock_DaemonCsgTransition", new { workerId, stage });

    /// <summary>记录请求提交（Info，携带选中的 worker 与剩余预算）。</summary>
    public static void SendStart(string requestId, string workerId, string cmd, double remainingSec)
        => _log("StateBlock_SendStart", new { requestId, workerId, cmd, remainingSec });

    /// <summary>记录请求成功完成（Info，携带耗时）。</summary>
    public static void SendDone(string requestId, string workerId, string cmd, double elapsedMs)
        => _log("StateBlock_SendDone", new { requestId, workerId, cmd, elapsedMs });

    /// <summary>记录基建故障（挂起/被杀）换 worker 重试（Warning）。</summary>
    public static void SendRetry(string requestId, string workerId, string cmd, int attempt, string? error)
        => _log("StateBlock_SendRetry", new { requestId, workerId, cmd, attempt, error });

    /// <summary>记录请求业务失败（Warning，daemon 正常返回业务错误，不重试）。</summary>
    public static void SendFailed(string requestId, string workerId, string cmd, string? error)
        => _log("StateBlock_SendFailed", new { requestId, workerId, cmd, error });

    /// <summary>记录请求被取消（Warning）。</summary>
    public static void SendCancelled(string requestId, string workerId, string cmd, string? error)
        => _log("StateBlock_SendCancelled", new { requestId, workerId, cmd, error });

    /// <summary>记录 worker 进程退出（被 kill/崩溃，Warning）。</summary>
    public static void WorkerExited(string workerId)
        => _log("StateBlock_WorkerExited", new { workerId });

    /// <summary>记录补位/重启失败（Warning）。</summary>
    public static void RespawnFailed(string workerId, string? error)
        => _log("StateBlock_RespawnFailed", new { workerId, error });

    /// <summary>记录非法 worker 状态迁移（Error，状态机一致性被破坏 = 代码 bug 信号）。</summary>
    public static void IllegalTransition(string workerId, string from, string to)
        => _log("StateBlock_IllegalTransition", new { workerId, from, to });

    /// <summary>记录池就绪成功（Info，携带耗时）。</summary>
    public static void PoolReady(int readyCount, double elapsedSec)
        => _log("StateBlock_PoolReady", new { readyCount, elapsedSec });

    /// <summary>记录背压等待开始（Warning，无可用就绪 worker 时）。</summary>
    public static void BackpressureWait(int readyCount)
        => _log("StateBlock_BackpressureWait", new { readyCount });

    /// <summary>记录补位/重启尝试（Info）。</summary>
    public static void Respawn(string workerId)
        => _log("StateBlock_Respawn", new { workerId });

    /// <summary>记录预热汇总（Info，成功数/总数）。</summary>
    public static void WarmupSummary(int warmed, int total)
        => _log("StateBlock_WarmupSummary", new { warmed, total });

    /// <summary>记录池初始化参数（Info）。</summary>
    public static void PoolInit(int poolSize, double idleTimeoutSec, double warmupBudgetSec, int infraRetries)
        => _log("StateBlock_PoolInit", new { poolSize, idleTimeoutSec, warmupBudgetSec, infraRetries });

    /// <summary>记录池销毁（Info）。</summary>
    public static void PoolDispose()
        => _log("StateBlock_PoolDispose", new { });
}

