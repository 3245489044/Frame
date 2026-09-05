using Plate.XmlMethod.Xml;

namespace Plate.XmlMethod.Svg
{
    /// <summary>
    /// SVG 便捷工厂：基于 <see cref="XmlElement"/> 产出常见 SVG 图元。
    /// 布局（坐标计算）由调用方负责，本层只负责"把图形以正确标签/属性产出"。
    /// 最终可用 <see cref="WrapHtml"/> 包装成自包含整页 HTML，直接交给 HtmlViewer(NavigateToString) 渲染。
    /// </summary>
    public static class Svg
    {
        // ---- 根 / 分组 ----

        public static XmlElement Root(double width, double height)
            => X.Elem("svg")
                .Attr("xmlns", "http://www.w3.org/2000/svg")
                .Attr("width", width).Attr("height", height)
                .Attr("viewBox", $"0 0 {width.ToString("0.##")} {height.ToString("0.##")}")
                // 布局不受宿主容器默认 line-height 干扰
                .Attr("style", "font-family: system-ui, sans-serif; display:block;");

        public static XmlElement Group(string? id = null)
        {
            var el = X.Elem("g");
            if (!string.IsNullOrEmpty(id)) el.Attr("id", id);
            return el;
        }

        /// <summary>非渲染定义容器（箭头 marker / 渐变 / clipPath 等需挂在 defs 下）。</summary>
        public static XmlElement Defs()
            => X.Elem("defs");

        /// <summary>箭头 marker：三角形箭头，fill 用 context-stroke 跟随描边色。供 Line/Path 加 marker-end 使用。</summary>
        public static XmlElement ArrowMarker(string id)
            => X.Elem("marker")
                .Attr("id", id)
                .Attr("viewBox", "0 0 10 10")
                .Attr("refX", "9").Attr("refY", "5")
                .Attr("markerWidth", "8").Attr("markerHeight", "8")
                .Attr("orient", "auto-start-reverse")
                .Attr("markerUnits", "userSpaceOnUse")
                .Child(Path("M 0 0 L 10 5 L 0 10 Z")
                    .Attrs(("fill", "context-stroke"), ("stroke", "none")));

        public static XmlElement Group(string id, Action<XmlElement> configure)
        {
            var el = Group(id);
            configure(el);
            return el;
        }

        // ---- 图形 ----

        public static XmlElement Rect(double x, double y, double width, double height, double? rx = null)
            => X.Elem("rect")
                .Attr("x", x).Attr("y", y)
                .Attr("width", width).Attr("height", height)
                .Attr("rx", rx);

        public static XmlElement Line(double x1, double y1, double x2, double y2)
            => X.Elem("line").Attr("x1", x1).Attr("y1", y1).Attr("x2", x2).Attr("y2", y2);

        public static XmlElement Circle(double cx, double cy, double r)
            => X.Elem("circle").Attr("cx", cx).Attr("cy", cy).Attr("r", r);

        public static XmlElement Path(string d)
            => X.Elem("path").Attr("d", d).Attr("fill", "none");

        public static XmlElement Polygon(params (double X, double Y)[] points)
        {
            var d = string.Join(" ", points.Select(p => $"{p.X.ToString("0.##")},{p.Y.ToString("0.##")}"));
            return X.Elem("polygon").Attr("points", d);
        }

        /// <summary>文本；x=居左/y=基线。常用例：节点标签。</summary>
        public static XmlElement Text(double x, double y, string content, string? anchor = "start")
            => X.Elem("text")
                .Attr("x", x).Attr("y", y)
                .Attr("text-anchor", anchor)
                .Text(content);

        // ---- 整页 HTML 包装 ----

        /// <summary>
        /// 把 SVG 字符串包装成一张自包含 HTML 整页（无外部 JS/CSS，仅有内联 SVG），
        /// 与 <c>Root(width,height)</c> 的 viewBox/model 保持一致，供 HtmlViewer 整页字符串注入。
        /// </summary>
        public static string WrapHtml(
            string svg,
            string title = "workflow",
            string? background = "#ffffff")
        {
            var cssBackground = background == null ? "" : $"body{{background:{background};}}";
            return
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                $"<title>{XmlText.Escape(title)}</title>" +
                "<style>html,body{margin:0;padding:8px;}" + cssBackground + "</style>" +
                "</head><body>" +
                svg +
                "</body></html>";
        }
    }

    /// <summary>面向 SVG 图元的链式样式扩展（描边/填充/虚线/圆角等）。</summary>
    public static class SvgStyleExtensions
    {
        public static XmlElement Fill(this XmlElement el, string? color)
            => color == null ? el : el.Attr("fill", color);

        public static XmlElement Stroke(this XmlElement el, string? color, double? width = null)
        {
            if (color != null) el.Attr("stroke", color);
            if (width != null) el.Attr("stroke-width", width);
            return el;
        }

        /// <summary>虚线描边（如降级/失败分支）：dash = "6 4"。</summary>
        public static XmlElement Dash(this XmlElement el, string dash = "6 4")
            => el.Attr("stroke-dasharray", dash);

        public static XmlElement Font(this XmlElement el, double? size = null, string? weight = null)
        {
            if (size != null) el.Attr("font-size", size);
            if (weight != null) el.Attr("font-weight", weight);
            return el;
        }
    }
}