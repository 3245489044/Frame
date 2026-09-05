using BackEndDriven.Diagnostics;
using FrameworkAbstractions.ComponentRegistry;
using Modules.Threading;
using Share.EventBindingOrchestrator.Abstractions;

namespace BackEndDriven.CommandExecution;

/// <summary>
/// 命令执行的统一调用结果。Result 为 null 表示 IEventPlatform 未注册（Error 携带原因）。
/// </summary>
public sealed record EventPlatformInvocation(FunctionCallFacadeResult? Result, string? Error)
{
    public bool PlatformAvailable => Result != null;
}

/// <summary>
/// 命令执行统一入口（聚拢：ExecSystem / CommandInputPort 都经此调用 IEventPlatform，
/// 消除 Resolve&lt;IEventPlatform&gt; + InvokeFunction{Async} + source/timeout 拼装的重复——R:1.2）。
/// 权威契约：TConfig.IEventPlatform.InvokeFunction{Async} 返回 FunctionCallFacadeResult。
/// </summary>
public static class EventPlatformExecutor
{
    /// <summary>统一调用来源（决策见 IEventPlatform，ExecCommand 语义=命令层外部函数执行）。</summary>
    public const string Source = "ExecCommand";

    /// <summary>默认超时（单一来源：<see cref="TimingDefaults.FunctionCallDefault"/>，对齐 IEventPlatform 契约 30s）。</summary>
    public static int DefaultTimeoutMs => (int)TimingDefaults.FunctionCallDefault.TotalMilliseconds;

    /// <summary>
    /// 同步执行函数。IEventPlatform 未注册时返回 PlatformAvailable=false 的调用结果。
    /// </summary>
    /// <param name="functionName">要执行的函数名。</param>
    /// <param name="parameters">函数参数；null 视为无参数。</param>
    /// <param name="timeoutMs">显式预算（ms）；null 用 <see cref="DefaultTimeoutMs"/>。</param>
    public static EventPlatformInvocation Invoke(
        string functionName,
        Dictionary<string, object?>? parameters,
        int? timeoutMs = null)
    {
        var platform = AppServiceProvider.Resolve<IEventPlatform>();
        if (platform == null)
        {
            BackEndLog.Log("EventPlatformExecutor_PlatformUnavailable", new { functionName });
            return new EventPlatformInvocation(
                Result: null,
                Error: "IEventPlatform 未注册到 AppServiceProvider");
        }

        var result = platform.InvokeFunction(functionName, parameters, Source, timeoutMs ?? DefaultTimeoutMs);
        return new EventPlatformInvocation(result, Error: null);
    }

    /// <summary>
    /// 异步执行函数。IEventPlatform 未注册时返回 PlatformAvailable=false 的调用结果。
    /// </summary>
    /// <param name="functionName">要执行的函数名。</param>
    /// <param name="parameters">函数参数；null 视为无参数。</param>
    /// <param name="timeoutMs">显式预算（ms）；null 用 <see cref="DefaultTimeoutMs"/>。</param>
    public static async Task<EventPlatformInvocation> InvokeAsync(
        string functionName,
        Dictionary<string, object?>? parameters,
        int? timeoutMs = null)
    {
        var platform = AppServiceProvider.Resolve<IEventPlatform>();
        if (platform == null)
        {
            BackEndLog.Log("EventPlatformExecutor_PlatformUnavailable", new { functionName });
            return new EventPlatformInvocation(
                Result: null,
                Error: "IEventPlatform 未注册到 AppServiceProvider");
        }

        var result = await platform.InvokeFunctionAsync(functionName, parameters, Source, timeoutMs ?? DefaultTimeoutMs)
            .ConfigureAwait(false);
        return new EventPlatformInvocation(result, Error: null);
    }
}
