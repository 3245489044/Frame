using System.Collections.Generic;
using System.Threading.Tasks;
using FrontEndDriven.Eventing;

namespace FrontEndDriven.Lifecycle;

/// <summary>
/// 生命周期钩子执行器：收敛为 <see cref="InitCommandExecutor"/> 的薄委托——
/// <c>lifecycle.onBeforeLoad/onAfterLoad</c> 与 <c>initCommands</c> 共用同一命令语义
/// （裸函数名 / `exec &lt;Func&gt; [k=v...] output=&lt;Control&gt;`），消除双轨差异。
/// </summary>
public static class LifecycleHookExecutor
{
    public static Task ExecuteHooksAsync(
        EventPlatform platform,
        IEnumerable<string>? hookNames,
        string scope)
        => InitCommandExecutor.ExecuteAllAsync(platform, hookNames, scope);
}