#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// HTML 后端运行时写回结果（plate 本地收敛状态，宿主零 Observe 依赖）。
/// 由注入的 <see cref="HtmlWritePump"/> 返回；其实现（连接层 FrontEndDriven）经 Observe 收敛协调器
/// 把 ObserveOutcome 映射为本状态，使 plate 不感知任何 Modules.Observe 类型。
/// </summary>
public readonly record struct HtmlWriteBackResult(
    HtmlWriteBackStatus Status,
    TimeSpan? Window = null,
    string? Detail = null);

/// <summary>宿主内容写回收敛状态（映射自 Observe 收敛语义，plate 本地独立枚举）。</summary>
public enum HtmlWriteBackStatus
{
    /// <summary>已收敛（Observe Committed：渲染完成 + 桥就绪）。</summary>
    Committed,
    /// <summary>显式降级（Observe Degraded：收敛窗口内未就绪，绝不静默成功）。</summary>
    Degraded,
    /// <summary>失败（Observe Failed：传输/取消/发送异常）。</summary>
    Failed,
}

/// <summary>
/// plate 本地写回端口（唯一 Observe 消费点抽象化）——HtmlBackendRuntime 只依赖本委托做宿主内容写回，
/// 收敛写真正语义（ObserveSessionManager + WebView2Transport）由连接层 FrontEndDriven 注入实现，
/// 依赖方向倒转为 上层(连接层/组合根) → plate 抽象。
/// </summary>
public delegate Task<HtmlWriteBackResult> HtmlWritePump(
    string html, CancellationToken ct = default);
