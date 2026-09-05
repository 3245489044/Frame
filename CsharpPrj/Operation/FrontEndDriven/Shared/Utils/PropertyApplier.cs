
#nullable enable

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FrontEndDriven.Abstractions;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Protocol;
using Modules.LayoutAnalysis.UIInspector;

namespace FrontEndDriven.Shared.Utils;

public static class PropertyApplier
{
    public static void ApplyProperties(
        Control control,
        Dictionary<string, object?> properties,
        IControlAdapter adapter,
        Point? ecsParentOrigin = null)
    {
        if (control == null) throw new ArgumentNullException(nameof(control));
        if (properties == null || properties.Count == 0) return;

        bool hasLeft = TryGetInt(properties, UIProtocol.Prop_Left, out var left);
        bool hasTop = TryGetInt(properties, UIProtocol.Prop_Top, out var top);
        bool hasWidth = TryGetInt(properties, UIProtocol.Prop_Width, out var width);
        bool hasHeight = TryGetInt(properties, UIProtocol.Prop_Height, out var height);

        if (hasLeft || hasTop || hasWidth || hasHeight)
        {
            int curLeft = hasLeft ? left : control.Left;
            int curTop = hasTop ? top : control.Top;
            int curWidth = hasWidth ? width : control.Width;
            int curHeight = hasHeight ? height : control.Height;

            // S3·布局应用类型感知：SetBounds 前按控件类型适配，保证「同一布局意图 → 同一物理结果」（StateSnapshot 确定性）。
            // TextBoxBase：AutoSize=true 会把 SetBounds 高度回缩为单行高（outputTextBox 412x21 根因），应用前强制关闭，
            // 布局高度统一由 ECS 驱动——行为适配从创建处（CreateControl）下沉到布局应用内核（单一权威）。
            if (control is TextBoxBase textBoxBase)
                textBoxBase.AutoSize = false;

            // F5·写路径 ECS 单一权威：优先用 ECS 父原点（纯 ECS）换算父相对 = 全局 form 坐标 − ECS 父原点，
            // 替代物理父链 ToParentRelative 换算——统一坐标写源，消除「物理父链未就位 → 把 ECS 全局坐标当父相对」漂移。
            // 无 ECS 上下文（测试态/装配期）回退物理父链换算，保持既有语义（契约不变）。
            var rel = ResolveParentRelative(control, curLeft, curTop, ecsParentOrigin);
            adapter.SetBounds(control, rel.X, rel.Y, curWidth, curHeight);
        }

        if (properties.TryGetValue(UIProtocol.Prop_Name, out var name) && name != null)
            control.Name = name.ToString();
        if (properties.TryGetValue(UIProtocol.Prop_Text, out var text) && text != null)
            adapter.SetText(control, text.ToString());
        if (properties.ContainsKey(UIProtocol.Prop_Enabled))
            adapter.SetEnabled(control, GetBool(properties, UIProtocol.Prop_Enabled));
        if (properties.ContainsKey(UIProtocol.Prop_Visible))
            adapter.SetVisible(control, GetBool(properties, UIProtocol.Prop_Visible));

        if (properties.TryGetValue(UIProtocol.Prop_FontFamily, out var ff) && ff != null)
            ApplyFontProperties(control, properties, ff.ToString(), adapter);
        else if (properties.ContainsKey(UIProtocol.Prop_FontSize) ||
                 properties.ContainsKey(UIProtocol.Prop_FontBold) ||
                 properties.ContainsKey(UIProtocol.Prop_FontItalic) ||
                 properties.ContainsKey(UIProtocol.Prop_FontUnderline))
        {
            var currentFontName = control.Font.FontFamily.Name;
            ApplyFontProperties(control, properties, currentFontName, adapter);
        }

        if (properties.TryGetValue(UIProtocol.Prop_ForeColor, out var fc) && fc != null)
            adapter.SetForeColor(control, fc.ToString());
        if (properties.TryGetValue(UIProtocol.Prop_BackColor, out var bc) && bc != null)
            adapter.SetBackColor(control, bc.ToString());

        // M2·布局意图化：原生 Dock/Anchor 接管（优先于像素 Bounds；dock/anchor 实体不产出 Left/Top/Width/Height）
        if (properties.TryGetValue(UIProtocol.Prop_Dock, out var dock)
            && dock is string dockStr
            && !string.IsNullOrWhiteSpace(dockStr)
            && Enum.TryParse<DockStyle>(dockStr, true, out var dockStyle))
        {
            control.Dock = dockStyle;
            FrontEndDrivenLog.Log("PropertyApplier_Dock_applied",
                new { controlName = control.Name, dock = dockStyle.ToString() });
        }
        else if (properties.TryGetValue(UIProtocol.Prop_Dock, out var dockRaw))
        {
            FrontEndDrivenLog.Log("PropertyApplier_Dock_parse_failed",
                new { controlName = control.Name, dockRaw = dockRaw?.ToString() });
        }

        if (properties.TryGetValue(UIProtocol.Prop_Anchor, out var anchor)
            && anchor is string anchorStr
            && !string.IsNullOrWhiteSpace(anchorStr))
        {
            control.Anchor = ParseAnchor(anchorStr);
        }

        if (TryGetInt(properties, UIProtocol.Prop_BorderWidth, out var borderWidth)
            && borderWidth > 0)
        {
            string borderColor = "Black";
            if (properties.TryGetValue(UIProtocol.Prop_BorderColor, out var bclr) && bclr != null)
                borderColor = bclr.ToString()!;
            adapter.SetBorderStyle(control, borderWidth, borderColor);
        }

        if (TryGetInt(properties, UIProtocol.Prop_SplitterDistance, out var dist))
            adapter.SetSplitterDistance(control, dist);
    }

    private static void ApplyFontProperties(
        Control control,
        Dictionary<string, object?> properties,
        string fontFamily,
        IControlAdapter adapter)
    {
        // 未指定 FontSize 时传 0，交由适配器兜底保留控件当前字号（原 9f 默认掩盖"未指定"状态）
        float fontSize = 0;
        if (properties.TryGetValue(UIProtocol.Prop_FontSize, out var fs) && fs != null)
            float.TryParse(fs.ToString(), out fontSize);
        bool bold = GetBool(properties, UIProtocol.Prop_FontBold);
        bool italic = GetBool(properties, UIProtocol.Prop_FontItalic);
        bool underline = GetBool(properties, UIProtocol.Prop_FontUnderline);
        adapter.SetFont(control, fontFamily, fontSize, bold, italic, underline);
    }

    /// <summary>解析 Anchor 组合字符串（如 "Left,Right,Top,Bottom"）为 AnchorStyles 位组合。</summary>
    private static AnchorStyles ParseAnchor(string value)
    {
        var result = AnchorStyles.None;
        foreach (var part in value.Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<AnchorStyles>(part, true, out var style))
                result |= style;
        }

        return result;
    }

    /// <summary>父相对解析：ECS 父原点优先（纯 ECS 单一权威）；无 ECS 上下文时物理父链 ToParentRelative 兜底。</summary>
    private static Point ResolveParentRelative(Control control, int left, int top, Point? ecsParentOrigin)
    {
        if (ecsParentOrigin.HasValue)
            return new Point(left - ecsParentOrigin.Value.X, top - ecsParentOrigin.Value.Y);
        return LayoutCoordinateResolver.ToParentRelative(control, left, top);
    }

    public static bool TryGetInt(Dictionary<string, object?> dict, string key, out int value)
    {
        value = 0;
        if (dict.TryGetValue(key, out var obj) && obj != null)
        {
            if (obj is int i) { value = i; return true; }
            return int.TryParse(obj.ToString(), out value);
        }
        return false;
    }

    public static string? GetString(Dictionary<string, object?> dict, string key)
    {
        if (dict.TryGetValue(key, out var value) && value != null)
            return value.ToString() ?? string.Empty;
        return string.Empty;
    }

    public static bool GetBool(Dictionary<string, object?> dict, string key)
    {
        if (dict.TryGetValue(key, out var value) && value != null)
        {
            if (value is bool b) return b;
            if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return false;
    }
}