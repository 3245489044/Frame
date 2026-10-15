#nullable enable

using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 刷新桥协议单一源（P1「声明接入」）——把「区域刷新」协议的**框架侧常量与属性串构造**从页面模板（JSON 手写）
/// 收敛到通用框架：模板只留 <c>{pollAttrs|raw}</c> 占位，值由本类构造、经渲染层注入。
/// <para>协议契约与 <c>RefreshBridgeScript</c> 消费端一一对应（cmd/subscribe/interval/throttle/ctx/ctx-attrs）。</para>
/// <para><b>常量权威</b>：值以 <c>Strategy.Refresh.Model.RefreshContract</c> 为权威（宿主两端同源目标）；
/// plate 位于 L1，不得上行引用 L3（会经传递闭包毒化全部 Share 消费方 → LB0001），
/// 故此处保留字面量并由 <c>RefreshFramework.Tests/ContractParityTests</c> 做**漂移对拍**（改权威值即测试红）。</para>
/// </summary>
public static class HtmlRefreshProtocol
{
    /// <summary>刷新命令（权威: Strategy.Refresh.Model.RefreshContract.Command；对齐 RefreshBridgeScript 的 <c>data-poll-cmd</c>）。</summary>
    public const string Command = "refresh";

    /// <summary>兜底轮询周期（ms）（权威: Strategy.Refresh.Model.RefreshContract.FallbackPollMs）。</summary>
    public const int IntervalMs = 30000;

    /// <summary>回投节流窗口（ms）（权威: Strategy.Refresh.Model.RefreshContract.ThrottleMs）。</summary>
    public const int ThrottleMs = 1000;

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
