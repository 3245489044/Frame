using CallCenter.Core.Discovery;
using CallCenter.Core.Tracks;
using ExecutionFramework.Abstractions;
using ExecutionFramework.Engine;

namespace CallCenter.Adapters;

/// <summary>
/// 「发现源 → CSharpTrack 执行后端」的单一装配 helper（SingleImplementation / TH-12，ANTI-DUPLICATED-IMPLEMENTATION）。
/// 收敛 Host CLI、Agent 组合根各自手写「有注册表 → ExecutionFramework 同栈；否则回落 Direct」的判断：宿主在每次
/// 热重载/热换后只需调 <see cref="Build"/>，让 CSharpTrack 与该轮发现源注册表同源装配 EF 后端（与 Func_Execute 同栈）。
/// </summary>
public static class ExecutionFrameworkInvokerFactory
{
    /// <summary>
    /// 构建 CSharpTrack 执行后端：发现源已真实执行 <c>RegisterAll</c>（<see cref="CSharpFunctionLibrarySource.Registry"/> 有值）
    /// → <see cref="ExecutionFrameworkDelegateInvoker"/>（统一超时/取消/异步解包/异常→ExecutionResult，EF 同栈）；
    /// 否则回落 <see cref="DirectDelegateInvoker"/>（零回归）。
    /// </summary>
    public static ICSharpDelegateInvoker Build(CSharpFunctionLibrarySource source)
    {
        if (source.Registry is { } registry)
        {
            return new ExecutionFrameworkDelegateInvoker(
                new FunctionExecutor(registry, new NullParameterProcessor(), new FallbackTypeConverter()));
        }

        return DirectDelegateInvoker.Instance;
    }
}
