
#nullable enable
using System;
using System.Text.Json;
using Common.PlaceholderFramework.Integrated.Handlers;

namespace WorkFlowMethod.Diagnostics
{
    /// <summary>
    /// WorkFlowMethod 统一日志门面 —— 全模块唯一日志入口。
    /// 走统一日志注册体系（<see cref="ModuleLog"/>）：消息模板注册在 workflowmethod_log_messages.json，
    /// keyPrefix=workflowmethod:，由 framework.config.json → logging.modules 单一注册源驱动，无需独立宿主注入。
    /// data 序列化为 JSON 字符串。该门面已取代旧的 IExecutionLogger / DebugLog / DebugWorkflowLogger / ILogger 平行日志通道。
    /// </summary>
    public static class WorkFlowMethodLog
    {
        private static readonly Func<string, object?, bool> _log = ModuleLog.Create("WorkFlowMethod", "workflowmethod:");

        /// <summary>记录一条结构化日志（按 messageId 在 JSON 中解析级别/分类/模板）。</summary>
        public static bool Log(string messageId, object? data = null)
            => _log(messageId, data);

        /// <summary>记录一条纯文本日志（messageId 在 JSON 中解析级别/分类，message 原样写入）。</summary>
        public static bool Log(string messageId, string message)
            => LogPlaceholderHandlerBase.Log(messageId, "WorkFlowMethod", "workflowmethod:", message);

        // ---- 会话级分类日志（替代原 DebugLog 的 Info/Warn/Debug/Error，统一落入本框架）----

        /// <summary>记录一条 Info 级分类日志。</summary>
        public static void Info(string category, string message, object? data = null)
            => _log("Wfm_SessionInfo", new { category, message, data = data is null ? "" : JsonSerializer.Serialize(data) });

        /// <summary>记录一条 Warning 级分类日志。</summary>
        public static void Warn(string category, string message, object? data = null)
            => _log("Wfm_SessionWarn", new { category, message, data = data is null ? "" : JsonSerializer.Serialize(data) });

        /// <summary>记录一条 Debug 级分类日志。</summary>
        public static void Debug(string category, string message, object? data = null)
            => _log("Wfm_SessionDebug", new { category, message, data = data is null ? "" : JsonSerializer.Serialize(data) });

        /// <summary>记录一条 Error 级分类日志（可选携带异常消息）。</summary>
        public static void Error(string category, string message, Exception? ex = null, object? data = null)
            => _log("Wfm_SessionError", new { category, message, error = ex?.Message ?? "", data = data is null ? "" : JsonSerializer.Serialize(data) });
    }
}
