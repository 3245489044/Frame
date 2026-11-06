
using Common.PlaceholderFramework.Integrated.Handlers;

namespace FrontEndDriven.Diagnostics;

/// <summary>
/// 本工程日志门面：仅保留模块名/KeyPrefix/消息词表；前缀剥离、模板落盘、未注册降级
/// 统一由 Share 的 <see cref="ModuleLog"/>（A-1 单源）承担。
/// </summary>
public static class FrontEndDrivenLog
{
    private const string ModuleName = "FrontEndDriven";
    private const string KeyPrefix = "fde:";

    private static readonly ModuleLogChannel Channel = ModuleLog.CreateChannel(ModuleName, KeyPrefix);

    public static void Initialize(string jsonFilePath) => Channel.Initialize(jsonFilePath);

    public static void Log(string messageId, object? data = null) => Channel.Log(messageId, data);

    public static void Log(string messageId, string message) => Channel.Log(messageId, message);
}
