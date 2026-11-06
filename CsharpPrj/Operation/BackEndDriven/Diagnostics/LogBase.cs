
#nullable enable
using Common.PlaceholderFramework.Integrated.Handlers;

namespace BackEndDriven.Diagnostics
{
    /// <summary>
    /// 本工程日志基类：前缀剥离 + 模板落盘 + 未注册降级 DEBUG 的单源实现已上移到 Share
    /// <see cref="ModuleLog.LogWithFallback"/>，此处仅保留工程内调用入口（IocLog/BackEndLog 共用）。
    /// </summary>
    internal static class LogBase
    {
        internal static void Log(string moduleName, string keyPrefix, string messageId, object? data = null)
            => ModuleLog.LogWithFallback(moduleName, keyPrefix, messageId, data);

        internal static void Log(string moduleName, string keyPrefix, string messageId, string message)
            => ModuleLog.LogWithFallback(moduleName, keyPrefix, messageId, message);
    }
}
