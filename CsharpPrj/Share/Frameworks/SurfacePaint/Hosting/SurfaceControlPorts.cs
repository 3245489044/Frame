#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;

namespace Share.SurfacePaint.Hosting;

/// <summary>
/// 控件事实快照（载体无关 DTO）：把 <c>ControlDescriptor</c> 降为中性数据，
/// 使策略层（Share）无需引用 FrontEndDriven。由载体在装配期填充。
/// </summary>
public sealed record SurfaceControlFacts(
    string Id,
    string TypeKey,
    string Name,
    string Text,
    string? FontFamily,
    float FontSize,
    bool FontBold,
    int? ForeColorArgb,
    int? BackColorArgb,
    string? ParentId,
    int Dpi,
    Rectangle Bounds,
    bool Visible,
    bool Enabled,
    IReadOnlyList<string> SkinRefs);

/// <summary>
/// 控件登记端口（载体实现：ControlPool / 控件树）。
/// <para>策略层只按 <c>Id</c> 查询事实，不持有任何平台类型。</para>
/// </summary>
public interface ISurfaceControlRegistry
{
    /// <summary>按控件 id 取事实（不存在 ⇒ null）。</summary>
    SurfaceControlFacts? Find(string controlId);

    /// <summary>按窗口 id 取可见控件事实（顺序由载体声明序决定）。</summary>
    IReadOnlyList<SurfaceControlFacts> VisibleControls(string windowId);
}

/// <summary>控件生命周期端口（载体实现：Paint/Disposed 事件、Invalidate、双缓冲）。</summary>
public interface ISurfaceControlLifecycle
{
    /// <summary>订阅绘制；同一控件重复订阅 ⇒ 忽略（幂等）。</summary>
    bool TrySubscribe(string controlId, Action<Graphics, Rectangle> paint, Action disposed);

    /// <summary>退订并释放绘制会话（幂等）。</summary>
    void Unsubscribe(string controlId);

    /// <summary>请求重绘。</summary>
    void Invalidate(string controlId);

    /// <summary>绘制前的宿主动作（如双缓冲）；返回可释放作用域。</summary>
    IDisposable? Prepare(string controlId);
}

/// <summary>窗口上下文端口（载体实现：ClientSize / DPI / 屏幕锚点 / 窗口级抓帧）。</summary>
public interface ISurfaceWindowContext
{
    /// <summary>窗口 id（与 <see cref="ISurfaceControlRegistry.VisibleControls"/> 同一键）。</summary>
    string WindowId { get; }

    /// <summary>窗口视口（DPI 已就绪）。</summary>
    SurfaceViewport Viewport { get; }

    /// <summary>窗口级背后快照（失败 ⇒ null；调用方负责记账）。</summary>
    SurfaceBackdropCapture? CaptureBackdrop(bool allowCached);
}

/// <summary>节拍端口（载体实现：System.Windows.Forms.Timer / 消息泵）。</summary>
public interface ISurfaceClock
{
    /// <summary>按间隔调度；返回可释放句柄即停止。</summary>
    IDisposable Schedule(TimeSpan interval, Action tick);
}

/// <summary>
/// 属性面端口（载体实现：Control.BackColor / ForeColor / Font / FlatAppearance）。
/// <para>不可表达 ⇒ false + reason（禁静默）。</para>
/// </summary>
public interface ISurfacePropertyTarget
{
    /// <summary>写底色（A=255）。</summary>
    bool TrySetBackColor(int argb, out string? reason);

    /// <summary>写前景色（A=255）。</summary>
    bool TrySetForeground(int argb, out string? reason);

    /// <summary>写字体。</summary>
    bool TrySetFont(string? family, float size, bool bold, bool italic, out string? reason);

    /// <summary>写按钮壳规格（Flat + BorderSize=0 + 最小高度）。</summary>
    bool TryApplyButtonSpec(int baseArgb, int borderArgb, out string? reason);
}
