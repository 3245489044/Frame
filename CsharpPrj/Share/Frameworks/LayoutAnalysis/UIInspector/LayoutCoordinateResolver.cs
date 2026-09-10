#nullable enable

using System.Drawing;
using System.Windows.Forms;

namespace Modules.LayoutAnalysis.UIInspector;

/// <summary>
/// 坐标换算单一出口（S2·SingleImplementation）：窗体相对 / 屏幕绝对 的统一换算。
/// 三个消费方（可见性检查 / 重叠检测 ControlTreeWalker / 位置报告）一律经此换算，禁止各自手写累加。
/// 基准语义：
///   - <see cref="ToFormRelative"/>：父链 .Location 累加（不含窗体自身）——不依赖控件句柄/窗体屏幕位置，SetBounds 后立即可靠；
///   - <see cref="ToScreen"/>：handle 已创建 → 控件自身 PointToScreen；未创建 → ToFormRelative + 所属窗体屏幕偏移
///     （显式回退策略，杜绝「已创建/未创建控件混合基准」导致的手工累加伪影）。
/// 注：ECS form 相对（ComputedLayoutComponent）为 ECS 原生语义直读（Left/Top 即 form 相对），无需换算出口，
/// 由消费方（LayoutInspectSystem）直接读取——本类不承载 ECS 类型依赖（Share.LayoutAnalysis 不引用 TConfig）。
/// </summary>
public static class LayoutCoordinateResolver
{
    /// <summary>
    /// 窗体相对边界：沿 Parent 链累加 .Location（不含窗体自身——窗体 .Left/.Top 是屏幕坐标，非布局坐标）。
    /// 与窗体屏幕位置/控件句柄无关，SetBounds 后立即可靠。
    /// </summary>
    public static Rectangle ToFormRelative(Control control)
    {
        if (control == null)
            throw new System.ArgumentNullException(nameof(control));

        int left = 0, top = 0;
        for (Control? c = control; c != null && c.Parent != null; c = c.Parent)
        {
            left += c.Left;
            top += c.Top;
        }
        return new Rectangle(left, top, control.Width, control.Height);
    }

    /// <summary>
    /// 控件 form 相对边界（SplitterPanel 感知版本）：递归父链，遇 SplitterPanel 经 SplitterDistance 换算
    /// （SplitterPanel .Left/.Top 恒 0，ToFormRelative 累加对其失真）。供 LayoutInspectSystem 物理对照使用，
    /// 与 ECS ComputedLayoutComponent（form 相对累积）同基准比对。
    /// </summary>
    public static Rectangle ToFormRelativeAccurate(Control control)
    {
        if (control == null)
            throw new System.ArgumentNullException(nameof(control));

        var origin = GetFormRelativeLocation(control);
        return new Rectangle(origin.X, origin.Y, control.Width, control.Height);
    }

    /// <summary>
    /// 父相对换算（M1·D7 修复）：将 ECS form 相对（累积）坐标换算为 WinForms 父相对应用坐标——布局应用层单一权威。
    /// 普通父链：rel = ecs - 父 form 相对位置（沿父链递归累加）；
    /// SplitterPanel 父特例：SplitterPanel 的 .Left/.Top 恒 0（位置由 SplitContainer 管理），
    ///   经 SplitContainer 的 SplitterDistance + SplitterWidth 换算面板 form 位置；
    /// 父为 null（顶层控件/宿主注入前）：返回原值（父相对 == form 相对，零偏移）。
    /// 注：ToFormRelative 累加 .Left/.Top 对 SplitterPanel 链失真，本换算内部使用
    ///   <see cref="GetFormRelativeLocation"/>（递归处理 SplitterPanel），不修改 ToFormRelative（消费方兼容）。
    /// </summary>
    public static Point ToParentRelative(Control control, int left, int top)
    {
        if (control == null)
            throw new System.ArgumentNullException(nameof(control));

        var parent = control.Parent;
        if (parent == null)
            return new Point(left, top);

        var parentOrigin = GetFormRelativeLocation(parent);
        return new Point(left - parentOrigin.X, top - parentOrigin.Y);
    }

    /// <summary>控件 form 相对位置（递归父链，遇 SplitterPanel 经 SplitterDistance 换算）。</summary>
    private static Point GetFormRelativeLocation(Control control)
    {
        if (control is SplitterPanel panel)
            return GetSplitterPanelFormLocation(panel);

        // 根节点（form 或未挂载顶层）：form 相对基点 (0,0)。
        // 不累加自身 Left/Top——form 的 Left/Top 是屏幕坐标（窗口居中时非 0），
        // 与 ToFormRelative 语义一致（循环不含窗体自身，SetBounds 后立即可靠）。
        if (control.Parent == null)
            return Point.Empty;

        var parentOrigin = GetFormRelativeLocation(control.Parent);
        return new Point(parentOrigin.X + control.Left, parentOrigin.Y + control.Top);
    }

    /// <summary>SplitterPanel 的 form 相对位置：.Left/.Top 恒 0，实际位置 = SplitContainer form 位置 + SplitterDistance/SplitterWidth 偏移。</summary>
    private static Point GetSplitterPanelFormLocation(SplitterPanel panel)
    {
        var parent = panel.Parent;
        var split = parent as SplitContainer;

        if (split == null)
            return new Point(panel.Left, panel.Top);

        var splitOrigin = GetFormRelativeLocation(split);
        if (split.Orientation == Orientation.Vertical)
        {
            return ReferenceEquals(panel, split.Panel2)
                ? new Point(splitOrigin.X + split.SplitterDistance + split.SplitterWidth, splitOrigin.Y)
                : splitOrigin;
        }

        return ReferenceEquals(panel, split.Panel2)
            ? new Point(splitOrigin.X, splitOrigin.Y + split.SplitterDistance + split.SplitterWidth)
            : splitOrigin;
    }

    /// <summary>
    /// 屏幕绝对坐标（显式 handle 回退策略）：
    /// handle 已创建 → control.PointToScreen(Point.Empty)（无累加误差，嵌套层级免疫）；
    /// 未创建 → ToFormRelative + 所属窗体屏幕位置（form.PointToScreen 偏移）；无窗体时退化为 form 相对（调用方自行判定）。
    /// </summary>
    public static Rectangle ToScreen(Control control)
    {
        if (control == null)
            throw new System.ArgumentNullException(nameof(control));

        if (control.IsHandleCreated)
        {
            var screenLocation = control.PointToScreen(Point.Empty);
            return new Rectangle(screenLocation.X, screenLocation.Y, control.Width, control.Height);
        }

        var form = control.FindForm();
        var formRelative = ToFormRelative(control);
        if (form == null)
            return formRelative; // 裸控件/设计时：无窗体可偏移，退化为 form 相对

        try
        {
            var formScreen = form.PointToScreen(Point.Empty);
            return new Rectangle(
                formScreen.X + formRelative.Left,
                formScreen.Y + formRelative.Top,
                control.Width,
                control.Height);
        }
        catch
        {
            // form 句柄未就绪（PointToScreen 抛 InvalidOperationException）：退化为 form 相对，不阻断换算
            return formRelative;
        }
    }
}
