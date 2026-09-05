
using System.Text.Json;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Integrated.Handlers;

namespace FrontEndDriven.Diagnostics;

public static class FrontEndDrivenLog
{
    private const string ModuleName = "FrontEndDriven";
    private const string KeyPrefix = "fde:";

    public static void Initialize(string jsonFilePath)
    {
        LogPlaceholderHandlerBase.Initialize(jsonFilePath, KeyPrefix);
    }

    private static string CleanMessageId(string messageId)
    {
        return messageId.StartsWith(KeyPrefix) ? messageId.Substring(KeyPrefix.Length) : messageId;
    }

    public static void Log(string messageId, object? data = null)
    {
        // L3 对齐：LogPlaceholderHandlerBase 消息定义未命中（loader 未初始化/msgId 未注册）时
        // 降级 DEBUG 落盘，与 BackEndDriven.LogBase 行为一致（消除静默丢失）。
        var cleanId = CleanMessageId(messageId);
        bool logged = LogPlaceholderHandlerBase.Log(cleanId, ModuleName, KeyPrefix, data);
        if (!logged)
        {
            var dataStr = data != null ? $" {JsonSerializer.Serialize(data, Plate.JsonMethod.Serialization.JsonSerializerFactory.Contexts.LogCompact)}" : "";
            PlaceholderLogHelper.Log("DEBUG", cleanId, ModuleName,
                $"[{KeyPrefix}{cleanId}]{dataStr}");
        }
    }

    public static void Log(string messageId, string message)
    {
        var cleanId = CleanMessageId(messageId);
        bool logged = LogPlaceholderHandlerBase.Log(cleanId, ModuleName, KeyPrefix, message);
        if (!logged)
        {
            PlaceholderLogHelper.Log("DEBUG", cleanId, ModuleName,
                $"[{KeyPrefix}{cleanId}] {message}");
        }
    }
}
