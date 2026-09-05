#nullable enable

using System.Net;
using System.Text;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// HTML 控件工厂——JSON type → DOM 模板的语义映射（与 WinForms ControlTypeRegistry 共用同一份 type 词表，
/// 差异仅在物理元素形态；缺省回退 div）。所有元素带 data-name（脚本 target / 参数 source 用）；
/// 文本/属性值一律 <see cref="WebUtility.HtmlEncode"/>（对齐既有可信通道 XSS 防线）。
/// P5：listview/combobox/treeview/tabcontrol 等 WinForms 化词表为<b>兼容迁移层</b>；新样式优先原生
/// <c>&lt;label&gt;/&lt;textarea&gt;/&lt;button&gt;</c> + CSS 类（本工厂多数 case 已产原生元素）。
/// </summary>
public static class HtmlControlFactory
{
    /// <summary>是否容器类型（子控件应渲染进内部）。容器词表唯一真源 = 共享语义层 <see cref="TConfig.Controls.ControlTypeCatalog"/>，
    /// 消除 HTML 侧硬编码副本（与 FrontEndDriven.ControlTypeRegistry 同源，避免词表与注册漂移）。</summary>
    public static bool IsContainer(string type) => TConfig.Controls.ControlTypeCatalog.IsContainer(type);

    /// <summary>
    /// 渲染单元素 HTML（含子控件递归，<paramref name="childrenHtml"/> 为已投影好的子元素 HTML）。
    /// <paramref name="styleCss"/> 为布局投影产出的定位样式（position:absolute;left..px;...）。
    /// </summary>
    public static string RenderElement(ControlConfig config, string styleCss, string childrenHtml = "")
    {
        var name = config.Name ?? string.Empty;
        var encodedName = WebUtility.HtmlEncode(name);
        var dataName = encodedName.Length > 0 ? $" data-name=\"{encodedName}\"" : string.Empty;
        // P5 布局语义化：声明 layout.intent 降为语义 intent，经 data-layout 交给页面 CSS/JS 解释（C# 不再产绝对像素；比例→px 换算保留在兼容层 ResolveRect）。
        if (!string.IsNullOrWhiteSpace(config.Layout?.Intent))
            dataName += $" data-layout=\"{WebUtility.HtmlEncode(config.Layout!.Intent)}\"";
        var text = WebUtility.HtmlEncode(config.Text ?? string.Empty);
        var type = (config.Type ?? "div").ToLowerInvariant();

        // P5 单协议：事件声明统一编译为 data-cmd（唯一交互协议，无 data-event 属性）；
        // cmd=event:{eventName} 由点击桥翻译回事件负载（{event,name,payload}）供事件桥处理（渲染端桥接：页面点击 postMessage 上报宿主）。
        var dataCmd = HasClickEvent(config) ? " data-cmd=\"event:click\"" : string.Empty;

        switch (type)
        {
            case "label":
                return $"<span{dataName} style=\"{styleCss}\">{text}</span>";

            case "textbox":
                return config.Multiline == true
                    ? $"<textarea{dataName} style=\"{styleCss}\">{text}</textarea>"
                    : $"<input type=\"text\"{dataName} style=\"{styleCss}\" value=\"{text}\">";

            case "button":
                return $"<button type=\"button\"{dataName}{dataCmd} style=\"{styleCss}\">{text}</button>";

            case "listview":
                return $"<ul{dataName} class=\"list\" style=\"{styleCss}\">{RenderItems(config.Items)}</ul>";

            case "combobox":
                return $"<select{dataName} style=\"{styleCss}\">{RenderOptions(config.Items, config.SelectedIndex)}</select>";

            case "treeview":
                return $"<ul{dataName} class=\"tree\" style=\"{styleCss}\">{RenderItems(config.Items)}</ul>";

            case "checkbox":
            case "radiobutton":
                var checkedAttr = config.Checked == true ? " checked" : string.Empty;
                // label 包裹 input：点击文本即切换勾选（可点区域/语义完整）；text 已 HtmlEncode。
                return $"<label style=\"{styleCss}\"><input type=\"{(type == "checkbox" ? "checkbox" : "radio")}\"{dataName}{dataCmd}{checkedAttr}>{text}</label>";

            case "numericupdown":
                return $"<input type=\"number\"{dataName} style=\"{styleCss}\" value=\"{WebUtility.HtmlEncode(config.Value.ToString())}\">";

            case "htmlviewer":
            case "markdownviewer":
            {
                // 富文本宿主：初始文本 encode 入内，运行期内容经 DOM 桥 set（innerHTML）注入；
                // 路线 A：声明 backend:"web" 时输出 data-backend，供页面 JS/CSS 走原生布局（而非绝对定位投影）。
                var backendAttr = string.IsNullOrWhiteSpace(config.Backend)
                    ? string.Empty
                    : $" data-backend=\"{WebUtility.HtmlEncode(config.Backend)}\"";
                return $"<div{dataName}{backendAttr} class=\"host\" style=\"{styleCss}\">{text}</div>";
            }

            case "tabcontrol":
                return RenderTabControl(config, dataName, styleCss, childrenHtml);

            default: // panel/groupbox/form/container/未知 → 容器或未知回退
                return $"<div{dataName} class=\"container\" style=\"{styleCss}\">{childrenHtml}</div>";
        }
    }

    private static string RenderTabControl(ControlConfig config, string dataName, string styleCss, string childrenHtml)
    {
        var sb = new StringBuilder();
        sb.Append("<div").Append(dataName).Append(" class=\"tabcontrol\" style=\"").Append(styleCss).Append("\">");
        sb.Append("<div class=\"tabbar\">");
        if (config.Controls is { Count: > 0 })
        {
            for (int i = 0; i < config.Controls.Count; i++)
            {
                var tab = config.Controls[i];
                sb.Append("<button type=\"button\" class=\"tab").Append(i == 0 ? " active" : string.Empty).Append("\" data-tab=\"").Append(i)
                  .Append("\">").Append(WebUtility.HtmlEncode(tab.Text ?? tab.Name ?? ("页 " + i))).Append("</button>");
            }
        }
        // childrenHtml 内每个 tab 页已隔离为独立 <div class="pane" data-pane={i}>（见 HtmlControlRenderer.RenderChildren），
        // 内联切换脚本（全局一次）把 tab 点击映射到对应 pane 显隐；pane 显隐规则见 HtmlLayoutProjector.BaseCss。
        sb.Append("</div><div class=\"panes\">").Append(childrenHtml).Append("</div>")
          .Append("\n<script>").Append(HtmlAssetRegistry.GetScript(HtmlAssetKeys.TabSwapScript)).Append("</script>\n</div>");
        return sb.ToString();
    }

    private static string RenderItems(IEnumerable<string>? items)
    {
        if (items is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var item in items)
            sb.Append("<li>").Append(WebUtility.HtmlEncode(item)).Append("</li>");
        return sb.ToString();
    }

    private static string RenderOptions(IEnumerable<string>? items, int? selectedIndex)
    {
        if (items is null) return string.Empty;
        var sb = new StringBuilder();
        int i = 0;
        foreach (var item in items)
        {
            var sel = i == (selectedIndex ?? -1) ? " selected" : string.Empty;
            sb.Append("<option value=\"").Append(WebUtility.HtmlEncode(item)).Append("\"").Append(sel)
              .Append(">").Append(WebUtility.HtmlEncode(item)).Append("</option>");
            i++;
        }
        return sb.ToString();
    }

    private static bool HasClickEvent(ControlConfig config)
        => config.Events?.EventsDictionary is { Count: > 0 } dict
           && dict.Keys.Any(k => string.Equals(k, "click", StringComparison.OrdinalIgnoreCase));
}
