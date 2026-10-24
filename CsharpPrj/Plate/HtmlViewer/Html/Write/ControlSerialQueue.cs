#nullable enable

using System;
using System.Collections.Concurrent;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 控件级串行队列（I3）：同一控件所有写共用一条串行通道；<see cref="UiWriteSource"/> 只决定合并优先级，
/// 不决定队列（禁按来源分多通道，避免交互与刷新双写竞态）。
/// <para>在飞期间到达的写合并为"待写槽"（按优先级择优）；被替换掉的内容计入 Dropped 并留可归因预览。</para>
/// </summary>
internal sealed class ControlSerialQueue
{
    private sealed class Slot
    {
        public readonly object Gate = new();
        public bool InFlight;
        public UiWriteSource InFlightSource;
        public string? Pending;
        public UiWriteSource PendingSource;
        public long Coalesced;
        public long Dropped;
        public string? LastDroppedPreview;
    }

    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>来源优先级（数值小者优先）：交互不得被在飞刷新合并掉。</summary>
    public static int Priority(UiWriteSource source) => source switch
    {
        UiWriteSource.Interactive => 0,
        UiWriteSource.Stream => 1,
        UiWriteSource.Refresh => 2,
        UiWriteSource.Tick => 3,
        _ => 4, // Mount
    };

    public bool IsInFlight(string control)
        => _slots.TryGetValue(control, out var slot) && slot.InFlight;

    /// <summary>尝试进入写（首入者得 first）；在飞则合入待写槽并返回 false（不递归）。</summary>
    public bool TryBegin(string control, string html, UiWriteSource source, out string first)
    {
        var slot = _slots.GetOrAdd(control, static _ => new Slot());
        lock (slot.Gate)
        {
            first = html;
            if (slot.InFlight)
            {
                slot.Coalesced++;
                if (slot.Pending is not null && Priority(source) > Priority(slot.PendingSource))
                {
                    slot.Dropped++;
                    slot.LastDroppedPreview = Preview(slot.Pending);
                }
                if (slot.Pending is null || Priority(source) <= Priority(slot.PendingSource))
                {
                    slot.Pending = html;
                    slot.PendingSource = source;
                }
                return false;
            }

            slot.InFlight = true;
            slot.InFlightSource = source;
            return true;
        }
    }

    /// <summary>取下一待写（有则继续 drain）；无则将控件置回空闲并返回 false。</summary>
    public bool TryDequeue(string control, out string next, out UiWriteSource nextSource)
    {
        if (!_slots.TryGetValue(control, out var slot))
        {
            next = string.Empty;
            nextSource = UiWriteSource.Refresh;
            return false;
        }

        lock (slot.Gate)
        {
            if (slot.Pending is not null)
            {
                next = slot.Pending;
                nextSource = slot.PendingSource;
                slot.Pending = null;
                return true;
            }

            slot.InFlight = false;
            next = string.Empty;
            nextSource = UiWriteSource.Refresh;
            return false;
        }
    }

    /// <summary>异常/收尾兜底：无条件置回空闲（幂等）；返回被丢弃的待写内容预览（null = 无待写）。</summary>
    public string? ForceExit(string control)
    {
        if (!_slots.TryGetValue(control, out var slot)) return null;
        lock (slot.Gate)
        {
            var dropped = slot.Pending;
            slot.InFlight = false;
            slot.Pending = null;
            return dropped is null ? null : Preview(dropped);
        }
    }

    /// <summary>取并清零合并/丢弃计数（可归因：丢弃内容留预览）。</summary>
    public (long Coalesced, long Dropped, string? DroppedPreview) TakeCounters(string control)
    {
        if (!_slots.TryGetValue(control, out var slot)) return (0, 0, null);
        lock (slot.Gate)
        {
            var counters = (slot.Coalesced, slot.Dropped, slot.LastDroppedPreview);
            slot.Coalesced = 0;
            slot.Dropped = 0;
            slot.LastDroppedPreview = null;
            return counters;
        }
    }

    private static string Preview(string html)
        => html.Length <= 80 ? html : html.Substring(0, 80) + "…";
}
