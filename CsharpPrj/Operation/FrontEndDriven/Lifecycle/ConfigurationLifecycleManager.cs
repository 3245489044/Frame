
#nullable enable

using System;
using System.Threading.Tasks;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Pipeline;
using TConfig;

namespace FrontEndDriven.Lifecycle;

/// <summary>
/// 启动编排（2026-08-06 M4b 收敛：移除 IScopeDestroyer 职责与未使用依赖，仅保留 BootstrapAsync）。
/// 作用域销毁统一经 StateCenter → EcsGate.DestroyScopeAsync → ICompositionLifecycle.ScopeDestroyed 单一路径，
/// 不再由本类承载销毁钩子编排（DestroyScopeAsync/IScopeDestroyer 已删除）。
/// 2026-08-06 M3 收敛：boot 两阶段渲染统一经 RenderRequestQueue FullLoad（StateCenter 事务化渲染），
/// 消除「boot 直连 executor vs 其他触发方入队」双轨（D6）。
/// </summary>
public sealed class ConfigurationLifecycleManager
{
    public async Task BootstrapAsync(UIConfiguration bootConfig)
    {
        ArgumentNullException.ThrowIfNull(bootConfig);

        if (bootConfig.Boot is not { Count: > 0 })
            return;

        var queue = ResolveQueue();
        foreach (var phase in bootConfig.Boot)
        {
            // F5（boot 串行就绪闸门）：await EnqueueFullLoad 的 TCS 结果契约保证——
            // 下一 phase 在上一 phase 完整渲染挂载（StateCenter 落 Loaded/Mounted）后才入队其宿主解析，
            // 杜绝「宿主未就绪即按名查找」的 boot 竞态噪音（D4）；无需额外显式调度。
            await queue.EnqueueFullLoad(phase.Scope, phase.Load, "boot").ConfigureAwait(true);
        }
    }

    private static RenderRequestQueue ResolveQueue()
        => AppServiceProvider.Resolve<RenderRequestQueue>()
            ?? throw new InvalidOperationException("RenderRequestQueue 未注册到 AppServiceProvider");
}
