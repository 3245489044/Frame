#nullable enable

using System.Threading;

namespace UiBackend.Html;

/// <summary>
/// 刷新链路统一追踪（L6 / D10）——一次刷新一条 <c>refreshTraceId</c>，贯穿「宿主推送 → 页面回投 → 事件桥路由 →
/// 业务 dispatch → 写回协调器」各跳，使"跳过原因/命中分支/结果状态"聚合为一条可检索记录（治"追溯需 20+ 跳"）。
/// <para>零依赖、进程内单调；由写回层与宿主日志门面共同消费。</para>
/// </summary>
public static class RefreshTrace
{
    private static long _seq;

    /// <summary>生成新 traceId（source = 归因标签，如 refresh/host）。</summary>
    public static string New(string source)
        => source + "-" + Interlocked.Increment(ref _seq).ToString("x8");
}
