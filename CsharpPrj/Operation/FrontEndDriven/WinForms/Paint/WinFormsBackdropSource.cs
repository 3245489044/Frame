#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Hosting;
using Share.SurfacePaint.Materials;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// WinForms 背后快照来源（<see cref="ISurfaceBackdropSource"/> 唯一实现）：
/// 父级自身表面 + 非本子树的兄弟内容（不新增合成抽象）。
/// <para>关键：**排除本控件的整棵子树**，避免默认底/WebView2/文字被玻璃洗白成鬼影。</para>
/// <para>边界：不穿越窗口/WebView2 HWND ⇒ 不做元素级看穿桌面。</para>
/// </summary>
internal sealed class WinFormsBackdropSource : ISurfaceBackdropSource
{
    /// <summary>WM_PRINT：把窗口自身画到指定 HDC（不带 PRF_CHILDREN 即不含任何子控件）。</summary>
    private const int WmPrint = 0x0317;

    /// <summary>PRF_CLIENT：绘制客户区。</summary>
    private const int PrfClient = 0x00000004;

    /// <summary>PRF_NONCLIENT：绘制非客户区（边框/滚动条）。</summary>
    private const int PrfNonClient = 0x00000002;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>快照生成期间的线程级重入闸（父级 owner-draw 不得再取快照）。</summary>
    [ThreadStatic]
    private static bool _capturingBackdrop;

    private static int _serial;

    private readonly Control _control;
    private readonly Action<bool> _suppress;
    private readonly Func<Control, ResolvedMaterial?> _materialOf;

    internal WinFormsBackdropSource(
        Control control, Action<bool> suppress, Func<Control, ResolvedMaterial?> materialOf)
    {
        _control = control;
        _suppress = suppress;
        _materialOf = materialOf;
    }

    /// <inheritdoc />
    public SurfaceBackdropCapture? Capture(SurfaceViewport viewport, bool allowCached)
    {
        var bounds = new Rectangle(0, 0, viewport.Width, viewport.Height);
        var bitmap = TryCaptureBehind(_control, bounds);
        if (bitmap is null) return null;
        return new SurfaceBackdropCapture(bitmap, Interlocked.Increment(ref _serial));
    }

    private Bitmap? TryCaptureBehind(Control control, Rectangle bounds)
    {
        if (_capturingBackdrop) return null; // 父级也挂玻璃材质时，避免逐层向上递归

        var parent = control.Parent;
        if (parent is null || !parent.IsHandleCreated) return null;

        var parentSize = parent.ClientSize;
        if (parentSize.Width <= 0 || parentSize.Height <= 0) return null;

        var target = new Rectangle(control.Location, bounds.Size);
        var source = Rectangle.Intersect(target, new Rectangle(Point.Empty, parentSize));
        if (source.Width <= 0 || source.Height <= 0) return null;

        Bitmap? parentBitmap = null;
        Bitmap? crop = null;
        _capturingBackdrop = true;
        _suppress(true);
        var page = SurfaceMaterialCatalog.PageBackground;
        var parentBackColor = parent.BackColor;
        // 父级背景仍是系统默认（未上色）⇒ 背后就是页面，快照必须以页面色起底，否则面板被浅灰洗白。
        var unstyled = page.HasValue && SurfaceHostCompositor.IsUnstyled(parentBackColor);
        try
        {
            parentBitmap = new Bitmap(parentSize.Width, parentSize.Height);
            if (page.HasValue)
            {
                using var prefill = Graphics.FromImage(parentBitmap);
                prefill.Clear(page.Value);
            }

            // 父级自身已是玻璃（含 backdrop 层）时，再拍进来就是「玻璃叠玻璃」：同一 fill 二次合成，
            // 面色被抬到 #29475F、背后渐变被抹平（panel/page 由 1.66 抬到 1.95）。此时只保留页面色起底，
            // 玻璃合成只做一层；父级非玻璃（实心/内容面）才需要拍自身表面。
            var parentMaterial = _materialOf(parent);
            var parentIsGlass = parentMaterial is not null && SurfacePaintSession.HasBackdrop(parentMaterial);
            if (unstyled) parent.BackColor = page!.Value;
            try
            {
                // 只打父级自身表面（不带 PRF_CHILDREN）：子控件一旦被拍进背板就会被玻璃洗白。
                if (!parentIsGlass || !page.HasValue)
                {
                    PaintParentSurface(parent, parentBitmap);
                }
            }
            finally
            {
                if (unstyled) parent.BackColor = parentBackColor;
            }

            // 兄弟内容仍属于「背后」：逐个按交集合成，跳过本控件所在祖先链（自身子树）。
            CompositeSiblings(parent, SubtreeRootIn(parent, control), source, parentBitmap);

            crop = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(crop))
            {
                graphics.DrawImage(
                    parentBitmap,
                    new Rectangle(source.X - target.X, source.Y - target.Y, source.Width, source.Height),
                    source, GraphicsUnit.Pixel);
            }

            return crop;
        }
        catch (Exception ex)
        {
            crop?.Dispose();
            FrontEndDrivenLog.Log("SkinDecorator_backdrop_snapshot_failed", new { message = ex.Message });
            return null;
        }
        finally
        {
            parentBitmap?.Dispose();
            _suppress(false);
            _capturingBackdrop = false;
        }
    }

    /// <summary>父级自身表面绘制（不含任何子控件）；子控件由 <see cref="CompositeSiblings"/> 单独合成。</summary>
    private static void PaintParentSurface(Control parent, Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try
        {
            SendMessage(parent.Handle, WmPrint, hdc, (IntPtr)(PrfClient | PrfNonClient));
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    /// <summary>本控件在父级中的直接子节点（祖先链根）：快照必须排除它整棵子树。</summary>
    private static Control? SubtreeRootIn(Control parent, Control control)
    {
        var node = control;
        while (node is not null && !ReferenceEquals(node.Parent, parent)) node = node.Parent;
        return node;
    }

    /// <summary>
    /// 合成父级的兄弟内容（跳过 <paramref name="excluded"/> 整棵子树）：只取与 <paramref name="source"/>
    /// 相交的部分，按 z 序后→前绘制。单个兄弟失败只记账，不中断整张背板。
    /// </summary>
    private static void CompositeSiblings(Control parent, Control? excluded, Rectangle source, Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        for (var i = parent.Controls.Count - 1; i >= 0; i--)
        {
            var sibling = parent.Controls[i];
            if (sibling is null || ReferenceEquals(sibling, excluded) || !sibling.Visible) continue;

            var bounds = sibling.Bounds;
            var inter = Rectangle.Intersect(bounds, source);
            if (inter.Width <= 0 || inter.Height <= 0) continue;

            try
            {
                using var slice = new Bitmap(inter.Width, inter.Height);
                // 目标矩形按控件原始尺寸偏移（负偏移即裁掉交集之外的部分），只渲染相交区域。
                sibling.DrawToBitmap(slice, new Rectangle(bounds.X - inter.X, bounds.Y - inter.Y, bounds.Width, bounds.Height));
                graphics.DrawImage(slice, inter.X, inter.Y);
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log("SkinDecorator_backdrop_sibling_failed",
                    new { type = sibling.GetType().Name, message = ex.Message });
            }
        }
    }
}
