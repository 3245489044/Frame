
#nullable enable
using System.Text.Json;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Integrated.Handlers;

namespace BackEndDriven.Diagnostics
{
    internal static class LogBase
    {
        /// <summary>
        /// 统一 messageId 前缀处理（L3 对齐）：剥掉模块前缀后再交由 LogPlaceholderHandlerBase
        /// （其消息定义按无前缀 msgId 索引，与 FrontEndDrivenLog 的 CleanMessageId 行为一致）。
        /// </summary>
        private static string CleanMessageId(string keyPrefix, string messageId)
        {
            return messageId.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)
                ? messageId.Substring(keyPrefix.Length)
                : messageId;
        }

        internal static void Log(string moduleName, string keyPrefix, string messageId, object? data = null)
        {
            var cleanId = CleanMessageId(keyPrefix, messageId);
            bool logged = LogPlaceholderHandlerBase.Log(cleanId, moduleName, keyPrefix, data);
            if (!logged)
            {
                var dataStr = data != null ? $" {JsonSerializer.Serialize(data, Plate.JsonMethod.Serialization.JsonSerializerFactory.Contexts.LogCompact)}" : "";
                PlaceholderLogHelper.Log("DEBUG", cleanId, moduleName,
                    $"[{keyPrefix}{cleanId}]{dataStr}");
            }
        }

        internal static void Log(string moduleName, string keyPrefix, string messageId, string message)
        {
            var cleanId = CleanMessageId(keyPrefix, messageId);
            bool logged = LogPlaceholderHandlerBase.Log(cleanId, moduleName, keyPrefix, message);
            if (!logged)
            {
                PlaceholderLogHelper.Log("DEBUG", cleanId, moduleName,
                    $"[{keyPrefix}{cleanId}] {message}");
            }
        }
    }
}
