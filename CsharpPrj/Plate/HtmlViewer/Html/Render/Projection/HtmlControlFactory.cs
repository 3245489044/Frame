#nullable enable

using System.Globalization;
using System.Text;
using Modules.Parsing.Markup;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// HTML 控件工厂——JSON type → DOM 模板的语义映射（与 WinForms ControlTypeRegistry 共用同一份 type 词表，
/// 差异仅在物理元素形态；缺省回退 div）。所有元素带 data-name（脚本 target / 参数 source 用）；
/// 标签/属性/文本一律经 <see cref="MarkupTag"/> + <see cref="MarkupAttrBatch"/>（属性名校验与编码单源）。
/// P5：listview/combobox/treeview/tabcontrol 等 WinForms 化词表为<b>兼容迁移层</b>；新样式优先原生
/// <c>&lt;span&gt;/&lt;textarea&gt;/&lt;button&gt;</c> + CSS 类（label 词表落原生 &lt;span&gt;，本工厂多数 case 已产原生元素）。
/// </summary>
public static class HtmlControlFactory
{
    /// <summary>控件发射方言：属性值编码同 HTML；空元素用 <c>&gt;</c>（与历史产物逐字一致）。</summary>
    private static readonly MarkupDialect ControlDialect = new(
        name: "html-control",
        encodeText: System.Net.WebUtility.HtmlEncode,
        encodeAttr: System.Net.WebUtility.HtmlEncode,
        boolStyle: MarkupBoolStyle.Bare,
        voidStyle: MarkupVoidStyle.Plain);

    /// <summary>是否容器类型（子控件应渲染进内部）。容器词表唯一真源 = 共享语义层 <see cref="TConfig.Controls.ControlTypeCatalog"/>。</summary>
    public static bool IsContainer(string type) => TConfig.Controls.ControlTypeCatalog.IsContainer(type);

    /// <summary>
    /// 渲染单元素 HTML（含子控件递归，<paramref name="childrenHtml"/> 为已投影好的子元素 HTML）。
    /// <paramref name="styleCss"/> 为布局投影产出的定位样式（position:absolute;left..px;...）。
    /// </summary>
    /// <param name="tabLabelFormat">tab 无文本时的回退文案格式（<c>{0}</c> = 序号；默认「页 {0}」）。</param>
    public static string RenderElement(
        ControlConfig config,
        string styleCss,
        string childrenHtml = "",
        string tabLabelFormat = "页 {0}")
    {
        var text = MarkupTag.Text(config.Text);
        var type = (config.Type ?? "div").ToLowerInvariant();
        var clickCmd = HasClickEvent(config) ? "event:click" : null;

        switch (type)
        {
            case "label":
                return MarkupTag.Element("span", WithStyle(BaseAttrs(config), styleCss), UiMarkup.Trusted(text), ControlDialect);

            case "textbox":
                return config.Multiline == true
                    ? MarkupTag.Element("textarea", WithStyle(BaseAttrs(config), styleCss), UiMarkup.Trusted(text), ControlDialect)
                    : MarkupTag.Void(
                        "input",
                        WithStyle(BaseAttrs(config, "text"), styleCss).Add("value", config.Text ?? string.Empty),
                        ControlDialect);

            case "button":
                return MarkupTag.Element(
                    "button",
                    WithStyle(BaseAttrs(config, "button").AddIfAny("data-cmd", clickCmd), styleCss),
                    UiMarkup.Trusted(text),
                    ControlDialect);

            case "listview":
                return MarkupTag.Element(
                    "ul",
                    WithStyle(BaseAttrs(config).Class("list", "list"), styleCss),
                    UiMarkup.Trusted(RenderItems(config.Items)),
                    ControlDialect);

            case "combobox":
                return MarkupTag.Element(
                    "select",
                    WithStyle(BaseAttrs(config), styleCss),
                    UiMarkup.Trusted(RenderOptions(config.Items, config.SelectedIndex)),
                    ControlDialect);

            case "treeview":
                return MarkupTag.Element(
                    "ul",
                    WithStyle(BaseAttrs(config).Class("tree", "tree"), styleCss),
                    UiMarkup.Trusted(RenderItems(config.Items)),
                    ControlDialect);

            case "checkbox":
            case "radiobutton":
            {
                var input = WithStyle(
                                 new MarkupAttrBatch(ControlDialect)
                                     .Add("type", type == "checkbox" ? "checkbox" : "radio")
                                     .Name(config.Name),
                                 styleCss: null)
                             .AddIfAny("data-cmd", clickCmd)
                             .Bool("checked", config.Checked == true);
                var label = MarkupTag.Element(
                    "label",
                    new MarkupAttrBatch(ControlDialect).Add("style", styleCss),
                    UiMarkup.Trusted(MarkupTag.Void("input", WithLayout(input, config), ControlDialect) + text),
                    ControlDialect);
                return label;
            }

            case "numericupdown":
                return MarkupTag.Void(
                    "input",
                    WithStyle(BaseAttrs(config, "number"), styleCss)
                        .Add("value", config.Value.ToString()),
                    ControlDialect);

            case "htmlviewer":
            case "markdownviewer":
            {
                // 富文本宿主：初始文本 encode 入内，运行期内容经 DOM 桥 set（innerHTML）注入；
                // 路线 A：声明 backend:"web" 时输出 data-backend，供页面 JS/CSS 走原生布局。
                var attrs = BaseAttrs(config)
                    .AddIfAny("data-backend", config.Backend)
                    .Class("host", "host");
                return MarkupTag.Element("div", WithStyle(attrs, styleCss), UiMarkup.Trusted(text), ControlDialect);
            }

            case "tabcontrol":
                return RenderTabControl(config, styleCss, childrenHtml, tabLabelFormat);

            default: // panel/groupbox/form/container/未知 → 容器或未知回退
                return MarkupTag.Element(
                    "div",
                    WithStyle(BaseAttrs(config).Class("container", "container"), styleCss),
                    UiMarkup.Trusted(childrenHtml),
                    ControlDialect);
        }
    }

    private static MarkupAttrBatch BaseAttrs(ControlConfig config, string? type = null)
    {
        var attrs = new MarkupAttrBatch(ControlDialect);
        if (type is not null) attrs.Add("type", type);
        attrs.Name(config.Name);
        return WithLayout(attrs, config);
    }

    private static MarkupAttrBatch WithLayout(MarkupAttrBatch attrs, ControlConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Layout?.Intent))
            attrs.Add("data-layout", config.Layout!.Intent);
        return attrs;
    }

    private static MarkupAttrBatch WithStyle(MarkupAttrBatch attrs, string? styleCss)
        => styleCss is null ? attrs : attrs.Add("style", styleCss);

    private static string RenderTabControl(
        ControlConfig config, string styleCss, string childrenHtml, string tabLabelFormat)
    {
        var sb = new StringBuilder();
        sb.Append(MarkupTag.Open(
            "div",
            WithStyle(BaseAttrs(config).Class("tabcontrol", "tabcontrol"), styleCss),
            ControlDialect));
        sb.Append(MarkupTag.Open("div", new MarkupAttrBatch(ControlDialect).Class("tabbar", "tabbar"), ControlDialect));
        if (config.Controls is { Count: > 0 })
        {
            for (var i = 0; i < config.Controls.Count; i++)
            {
                var tab = config.Controls[i];
                var label = tab.Text ?? tab.Name ?? string.Format(CultureInfo.InvariantCulture, tabLabelFormat, i);
                var attrs = new MarkupAttrBatch(ControlDialect)
                    .Add("type", "button")
                    .Add("class", i == 0 ? "tab active" : "tab")
                    .Add("data-tab", i.ToString(CultureInfo.InvariantCulture));
                sb.Append(MarkupTag.Element("button", attrs, UiMarkup.Trusted(MarkupTag.Text(label)), ControlDialect));
            }
        }

        // childrenHtml 内每个 tab 页已隔离为独立 <div class="pane" data-pane={i}>（见 HtmlControlRenderer.RenderChildren）。
        sb.Append(MarkupTag.Close("div", ControlDialect));
        sb.Append(MarkupTag.Open("div", new MarkupAttrBatch(ControlDialect).Class("panes", "panes"), ControlDialect));
        sb.Append(childrenHtml);
        sb.Append(MarkupTag.Close("div", ControlDialect));
        sb.Append('\n');
        sb.Append(MarkupTag.Element(
            "script", null, UiMarkup.Trusted(HtmlAssetRegistry.GetScript(HtmlAssetKeys.TabSwapScript)), ControlDialect));
        sb.Append('\n');
        sb.Append(MarkupTag.Close("div", ControlDialect));
        return sb.ToString();
    }

    private static string RenderItems(IEnumerable<string>? items)
    {
        if (items is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var item in items)
            sb.Append(MarkupTag.Element("li", null, UiMarkup.Trusted(MarkupTag.Text(item)), ControlDialect));
        return sb.ToString();
    }

    private static string RenderOptions(IEnumerable<string>? items, int? selectedIndex)
    {
        if (items is null) return string.Empty;
        var sb = new StringBuilder();
        var i = 0;
        foreach (var item in items)
        {
            var attrs = new MarkupAttrBatch(ControlDialect)
                .Add("value", item)
                .Bool("selected", i == (selectedIndex ?? -1));
            sb.Append(MarkupTag.Element("option", attrs, UiMarkup.Trusted(MarkupTag.Text(item)), ControlDialect));
            i++;
        }

        return sb.ToString();
    }

    private static bool HasClickEvent(ControlConfig config)
        => config.Events?.EventsDictionary is { Count: > 0 } dict
           && dict.Keys.Any(k => string.Equals(k, "click", StringComparison.OrdinalIgnoreCase));
}
