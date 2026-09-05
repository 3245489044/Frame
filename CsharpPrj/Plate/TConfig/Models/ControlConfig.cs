
using System.Text.Json.Serialization;

namespace TConfig;

public class ControlConfig
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ToolTip { get; set; }
    public string? Placeholder { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; } = 100;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 23;

    public string Dock { get; set; } = "None";

    public string Anchor { get; set; } = "Top, Left";

    public LayoutPosition? Layout { get; set; }
    public StyleConfig? Style { get; set; }
    public DataBindingConfig? DataBinding { get; set; }
    public EventConfig? Events { get; set; }

    [JsonPropertyName("controls")]
    public List<ControlConfig>? Controls { get; set; }

    [JsonPropertyName("items")]
    public List<string>? Items { get; set; }

    public List<ColumnConfig>? Columns { get; set; }
    public List<TabPageConfig>? Tabs { get; set; }
    public List<ListViewColumnConfig>? ListViewColumns { get; set; }
    public bool? Checked { get; set; }

    /// <summary>checkedlistbox 预勾选项。</summary>
    [JsonPropertyName("checkedItems")]
    public List<string>? CheckedItems { get; set; }
    public bool? Multiline { get; set; }
    public string? ScrollBars { get; set; }
    public bool? FullRowSelect { get; set; }
    public bool? GridLines { get; set; }
    public bool? Enabled { get; set; } = true;
    public bool? ReadOnly { get; set; }
    public bool? AcceptsReturn { get; set; }
    public int? MaxLength { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public int Value { get; set; }
    public int ViewIndex { get; set; }
    public string? BackColor { get; set; }
    public string? ForeColor { get; set; }
    public int? SelectedIndex { get; set; }

    /// <summary>combobox 下拉样式（DropDown/DropDownList/Simple）。缺省 DropDown（WinForms 默认）。</summary>
    public string? DropDownStyle { get; set; }

    [JsonPropertyName("stackLayout")]
    public StackLayoutConfig? StackLayout { get; set; }

    [JsonPropertyName("container")]
    public ContainerConfig? Container { get; set; }

    public string? View { get; set; }

    public string? Orientation { get; set; }

    public double? SplitterDistance { get; set; }

    [JsonPropertyName("functionName")]
    public string? FunctionName { get; set; }

    public string? FixedPanel { get; set; }

    public bool? IsSplitterFixed { get; set; }

    public List<ControlConfig>? Panel1Controls { get; set; }

    public List<ControlConfig>? Panel2Controls { get; set; }

    public Dictionary<string, string>? ExtensionData { get; set; }

    /// <summary>可见性语义声明（可选字符串）：expect / expect-visible / semantic / semantic-hidden / deferred / deferred-hidden。缺省按类型/运行时语义解析。</summary>
    [JsonPropertyName("hiddenSemantics")]
    public string? HiddenSemantics { get; set; }

    /// <summary>渲染后端声明（路线标记）：缺省/空 = 默认（绝对定位投影）；"web" = 页面内 JS/CSS 原生布局（路线 A，展示/会话类页）。</summary>
    [JsonPropertyName("backend")]
    public string? Backend { get; set; }

    /// <summary>htmlviewer/markdownviewer 内部页面布局声明（JSON 控件树，经 HTML 后端投影；
    /// 缺省 null = 无内部声明，控件仅作富文本宿主）。[HtmlPageDeclaration 元素复用本类，HtmlControlFactory/HtmlLayoutProjector 直接消费]</summary>
    [JsonPropertyName("htmlPage")]
    public HtmlPageDeclaration? HtmlPage { get; set; }
}

/// <summary>
/// HTML 内部页面声明：htmlviewer 内部布局 = 控件树 + 数据占位（{name} 注入点）。
/// 元素复用 <see cref="ControlConfig"/>（HtmlControlFactory/HtmlLayoutProjector 直接消费，零新渲染语义）。
/// </summary>
public sealed class HtmlPageDeclaration
{
    /// <summary>内部页面名（供 <c>HtmlDeclarationParser.ExtractHtmlPage(host, pageName)</c> 定位；缺省 null 匹配任意）。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("controls")]
    public List<ControlConfig>? Controls { get; set; }

    /// <summary>内部页面投影容器尺寸（像素）；0 → 投影兜底 1200/800（对齐 HtmlUiProjector 默认）。</summary>
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}
