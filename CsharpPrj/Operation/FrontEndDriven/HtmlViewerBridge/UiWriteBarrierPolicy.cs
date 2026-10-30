#nullable enable

using System;
using Modules.Observe;
using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 写屏障策略（通用件，声明单源）—— 把「写回来源 → 收敛屏障模式与窗口」从写回泵方法体收敛为可注入声明：
/// 交互/流走 FireAndForget（不等待屏障），刷新走窄窗，挂载/其它走较长窗；宿主可整体替换（测试/慢机调参）。
/// </summary>
/// <param name="RefreshWindow">刷新来源窗口（缺省 250ms：交互已走直写，刷新无需长窗）。</param>
/// <param name="MountWindow">挂载/其它来源窗口（缺省 5s）。</param>
/// <param name="WindowSelector">按来源显式分档（非 null 时优先于两个固定窗口）。</param>
public sealed record UiWriteBarrierPolicy(
    TimeSpan? RefreshWindow = null,
    TimeSpan? MountWindow = null,
    Func<UiWriteSource, TimeSpan?>? WindowSelector = null)
{
    /// <summary>框架默认策略（交互/流不等待；刷新 250ms；挂载 5s）。</summary>
    public static readonly UiWriteBarrierPolicy Default = new();

    /// <summary>来源 → 屏障选项（受控词映射单点；窗口取值见 <see cref="ObserveBarrierDefaults"/>）。</summary>
    public WriteOptions For(UiWriteSource source)
        => source switch
        {
            UiWriteSource.Interactive or UiWriteSource.Stream =>
                new WriteOptions(BarrierMode.FireAndForget, TimeSpan.FromMilliseconds(ObserveBarrierDefaults.FireAndForgetWindowMs)),
            UiWriteSource.Refresh =>
                new WriteOptions(BarrierMode.Timeout,
                    WindowSelector?.Invoke(source)
                    ?? RefreshWindow
                    ?? TimeSpan.FromMilliseconds(ObserveBarrierDefaults.RefreshWindowMs)),
            _ => new WriteOptions(BarrierMode.Timeout,
                    WindowSelector?.Invoke(source)
                    ?? MountWindow
                    ?? TimeSpan.FromSeconds(ObserveBarrierDefaults.MountWindowSeconds)),
        };
}
