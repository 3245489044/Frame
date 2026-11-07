#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Plate.HtmlViewer;

/// <summary>
/// 存活 <see cref="HtmlViewer"/> 实例注册表（弱引用，不延长控件寿命）。
/// <para>供运行期逐页推送使用：同一进程内可能有对话页 / 日志页 / 命令帮助页多个 WebView，
/// 推送必须按实例各自换算锚点，不能用模块级单一值。</para>
/// </summary>
public static class HtmlViewerLiveRegistry
{
    private static readonly ConcurrentDictionary<Guid, WeakReference<HtmlViewer>> Entries = new();

    /// <summary>控件构造时登记（无需 Dispose 反注册；<see cref="Snapshot"/> 会剪枝）。</summary>
    public static void Register(HtmlViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        Entries[viewer.InstanceId] = new WeakReference<HtmlViewer>(viewer);
    }

    /// <summary>页面内容写入完成（整页导航后 inline 锚点会丢失；订阅方应清缓存并重推）。</summary>
    public static event Action<HtmlViewer>? ContentWritten;

    /// <summary>由 <see cref="HtmlViewer.SetHtmlAsync"/> 完成后调用。</summary>
    public static void NotifyContentWritten(HtmlViewer viewer)
    {
        ContentWritten?.Invoke(viewer);
    }

    /// <summary>当前存活实例快照（剔除已释放/已回收项）。</summary>
    public static IReadOnlyList<HtmlViewer> Snapshot()
    {
        var live = new List<HtmlViewer>(Entries.Count);
        foreach (var pair in Entries)
        {
            HtmlViewer? viewer = null;
            var alive = pair.Value.TryGetTarget(out viewer);
            if (!alive || viewer is null || viewer.IsDisposed)
            {
                Entries.TryRemove(pair.Key, out _);
                continue;
            }

            live.Add(viewer);
        }

        return live;
    }
}
