#nullable enable

using System.Text;
using Strategy.Refresh.Model;

namespace UiBackend.Html;

/// <summary>
/// 刷新桥协议单一源（P1「声明接入」）——把「区域刷新」协议的**框架侧常量与属性串构造**从页面模板（JSON 手写）
/// 收敛到通用框架：模板只留 <c>{pollAttrs|raw}</c> 占位，值由本类构造、经渲染层注入。
/// <para>协议契约与 <c>RefreshBridgeScript</c> 消费端一一对应（cmd/subscribe/interval/throttle/ctx/ctx-attrs）。</para>
/// <para><b>常量单源</b>：cmd / throttle / interval 一律取自 <see cref="RefreshContract"/>（宿主 C# 与页面 JS 两端同源）。</para>
/// </summary>
public static class HtmlRefreshProtocol
{
    /// <summary>刷新命令（对齐 RefreshBridgeScript 的 <c>data-poll-cmd</c>）。</summary>
    public const string Command = RefreshContract.Command;

    /// <summary>兜底轮询周期（ms）——页面在无宿主推送时按此周期回投刷新（对齐 <c>data-poll-interval</c>）。</summary>
    public const int IntervalMs = RefreshContract.FallbackPollMs;

    /// <summary>回投节流窗口（ms）——窗口内多次变更合并为一次回投（对齐 <c>data-refresh-throttle</c>）。</summary>
    public const int ThrottleMs = RefreshContract.ThrottleMs;

    /// <summary>
    /// 构造区域刷新协议属性串（注入模板占位）：框架常量（cmd/interval/throttle）+ 域声明（subscribe 订阅键、
    /// ctx 选择器、ctx-attrs 上下文属性名）。<paramref name="ctxSelector"/> / <paramref name="ctxAttrs"/> 空 ⇒ 省略。
    /// </summary>
    public static string PollAttributes(string subscribe, string? ctxSelector, string? ctxAttrs)
    {
        var sb = new StringBuilder();
        sb.Append("data-poll-cmd=\"").Append(Command).Append('"');
        sb.Append(" data-poll-subscribe=\"").Append(subscribe).Append('"');
        sb.Append(" data-refresh-throttle=\"").Append(ThrottleMs).Append('"');
        sb.Append(" data-poll-interval=\"").Append(IntervalMs).Append('"');
        if (!string.IsNullOrEmpty(ctxSelector)) sb.Append(" data-poll-ctx=\"").Append(ctxSelector).Append('"');
        if (!string.IsNullOrEmpty(ctxAttrs)) sb.Append(" data-poll-ctx-attrs=\"").Append(ctxAttrs).Append('"');
        return sb.ToString();
    }
}
