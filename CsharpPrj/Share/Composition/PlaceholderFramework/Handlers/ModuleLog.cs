
using System.Text.Json;
using Common.PlaceholderFramework.Core;

namespace Common.PlaceholderFramework.Integrated.Handlers
{
    /// <summary>
    /// 模块日志门面（Share A-1/A-2 单源）：把「剥 KeyPrefix → LogPlaceholderHandlerBase 结构化落盘 →
    /// 未注册时降级 DEBUG 落盘（不静默丢失）」收敛为一份实现。
    /// <para>
    /// Operation 各工程的 <c>XxxLog</c> 只保留模块名/KeyPrefix/消息词表，统一委托到本门面。
    /// </para>
    /// </summary>
    public static class ModuleLog
    {
        private static readonly JsonSerializerOptions FallbackJsonOptions = new()
        {
            WriteIndented = false
        };

        /// <summary>兼容旧入口：忽略 KeyPrefix 剥离，直接调用（无降级）。优先用 <see cref="LogWithFallback"/>。</summary>
        public static bool Log(string msgId, object? parameters = null, string moduleName = "WdUI", string keyPrefix = "wdui:")
            => LogPlaceholderHandlerBase.Log(msgId, moduleName, keyPrefix, parameters);

        /// <summary>
        /// 创建模块日志通道（推荐）：绑定模块名与 KeyPrefix，内置前缀剥离与降级落盘。
        /// </summary>
        public static ModuleLogChannel CreateChannel(string moduleName, string keyPrefix)
            => new(moduleName, keyPrefix);

        /// <summary>创建无状态委托（兼容既有 <c>_log</c> 字段用法；现在带降级落盘）。</summary>
        public static Func<string, object?, bool> Create(string moduleName, string keyPrefix)
            => (msgId, parameters) => LogWithFallback(moduleName, keyPrefix, msgId, parameters);

        /// <summary>
        /// 结构化日志（带降级）：先按注册模板落盘；未注册/loader 未初始化 → 以 DEBUG 落原始 payload。
        /// </summary>
        public static bool LogWithFallback(string moduleName, string keyPrefix, string messageId, object? parameters = null)
        {
            var cleanId = CleanMessageId(keyPrefix, messageId);
            bool logged = LogPlaceholderHandlerBase.Log(cleanId, moduleName, keyPrefix, parameters);
            if (logged) return true;

            string dataStr;
            if (parameters is null)
            {
                dataStr = string.Empty;
            }
            else
            {
                try
                {
                    dataStr = " " + JsonSerializer.Serialize(
                        parameters,
                        Plate.JsonMethod.Serialization.JsonSerializerFactory.Contexts.LogCompact);
                }
                catch (Exception)
                {
                    // 模板未注册时的兜底路径，序列化失败不能反噬业务；退化为 ToString。
                    dataStr = " " + parameters;
                }
            }

            PlaceholderLogHelper.Log("DEBUG", cleanId, moduleName, $"[{keyPrefix}{cleanId}]{dataStr}");
            return true;
        }

        /// <summary>纯文本日志（带降级）。</summary>
        public static bool LogWithFallback(string moduleName, string keyPrefix, string messageId, string message)
        {
            var cleanId = CleanMessageId(keyPrefix, messageId);
            bool logged = LogPlaceholderHandlerBase.Log(cleanId, moduleName, keyPrefix, message);
            if (logged) return true;

            PlaceholderLogHelper.Log("DEBUG", cleanId, moduleName, $"[{keyPrefix}{cleanId}] {message}");
            return true;
        }

        internal static string CleanMessageId(string keyPrefix, string messageId)
            => messageId.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)
                ? messageId.Substring(keyPrefix.Length)
                : messageId;
    }

    /// <summary>
    /// 事实出口门面（Share A-3）：为「注入 sink」类诊断（如存储一致性事件）提供统一落盘实现，
    /// 组合根可将 <c>StorageConsistencyEvents.Sink</c> 接到本出口，避免各工程再写一份 sink。
    /// </summary>
    public static class FactSink
    {
        /// <summary>创建 (事件名, 负载) 出口委托。</summary>
        public static Action<string, object?> Create(string moduleName, string keyPrefix)
            => (eventName, payload) => ModuleLog.LogWithFallback(moduleName, keyPrefix, eventName, payload);
    }

    /// <summary>绑定模块名/KeyPrefix 的日志通道（无状态、线程安全；Initialize 需在启动期调用一次）。</summary>
    public sealed class ModuleLogChannel
    {
        internal ModuleLogChannel(string moduleName, string keyPrefix)
        {
            ModuleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
            KeyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        }

        public string ModuleName { get; }

        public string KeyPrefix { get; }

        /// <summary>初始化本模块的日志消息模板文件（幂等，与既有 LogPlaceholderHandlerBase.Initialize 一致）。</summary>
        public void Initialize(string jsonFilePath)
            => LogPlaceholderHandlerBase.Initialize(jsonFilePath, KeyPrefix);

        public bool Log(string messageId, object? parameters = null)
            => ModuleLog.LogWithFallback(ModuleName, KeyPrefix, messageId, parameters);

        public bool Log(string messageId, string message)
            => ModuleLog.LogWithFallback(ModuleName, KeyPrefix, messageId, message);
    }
}
