#nullable enable

using System;
using System.Collections.Concurrent;

namespace FrontEndDriven.Shared.Framework;

/// <summary>
/// 通用节流门（集中管理散落的刷新节流）：按 key 的滑动窗口次数上限。
/// 收敛 RenderVisibilityCheckStage 自愈 2s 节流、FunctionExecutedRenderBridge 重渲染封顶等
/// 「窗口内每 key 最多 N 次」语义到单一可复用组件（统一窗口、统一上限、统一重置）。
/// 线程安全：ConcurrentDictionary + CAS 更新，可从任意线程调用。
/// </summary>
public sealed class ThrottleGate
{
    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly int _maxInWindow;
    private readonly long _windowMs;

    private sealed record Slot(long StartTicks, int Count);

    /// <param name="maxInWindow">窗口内每 key 允许的最大进入次数（≥1）。</param>
    /// <param name="window">滑动窗口时长。</param>
    public ThrottleGate(int maxInWindow, TimeSpan window)
    {
        _maxInWindow = Math.Max(1, maxInWindow);
        _windowMs = Math.Max(1, (long)window.TotalMilliseconds);
    }

    /// <summary>
    /// 尝试进入（放行一次）。窗口内已达上限 → false；窗口过期 → 重置新窗口并放行。
    /// 单一 key 传同一字符串（如 string.Empty）即实现「全局单槽」语义。
    /// </summary>
    public bool TryEnter(string key)
    {
        var now = Environment.TickCount64;

        while (true)
        {
            var slot = _slots.GetOrAdd(key, _ => new Slot(now, 0));

            if (now - slot.StartTicks > _windowMs)
            {
                // 窗口过期：重置为新窗口（计 1）并放行。
                if (_slots.TryUpdate(key, new Slot(now, 1), slot))
                    return true;
                continue;
            }

            if (slot.Count < _maxInWindow)
            {
                if (_slots.TryUpdate(key, new Slot(slot.StartTicks, slot.Count + 1), slot))
                    return true;
                continue;
            }

            return false;
        }
    }

    /// <summary>清空全部计数（测试/生命周期重置用）。</summary>
    public void Reset() => _slots.Clear();
}
