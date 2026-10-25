
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

    /// <summary>写回方式声明（htmlviewer/markdownviewer 宿主控件）：缺省/空 = <c>navigate</c>（整页重载，零回归）；
    /// <c>patch</c> = 内容补丁（仅替换 body/容器 innerHTML，保存并恢复滚动、&lt;details&gt; 展开与焦点态）——
    /// 供事件驱动的刷新写回使用，消除 NavigateToString 整文档重载导致的 UI 位置重置。</summary>
    [JsonPropertyName("writeback")]
    public string? Writeback { get; set; }

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

    /// <summary>
    /// 页面受控态声明（B2）：槽名 → 默认值 + 归一化开关。装配期与页面回投 <c>uiState</c> 做键集合对拍，
    /// 未声明键不落状态（禁隐式状态）。
    /// </summary>
    [JsonPropertyName("state")]
    public Dictionary<string, HtmlPageStateSlot>? State { get; set; }

    /// <summary>
    /// 页面区域声明（B4）：写回目标 <c>region:&lt;key&gt;</c> 的合法域；未声明区域写入 → 装配失败。
    /// </summary>
    [JsonPropertyName("regions")]
    public List<HtmlPageRegionDeclaration>? Regions { get; set; }
    /// <summary>
    /// 页面级事件目标声明（R4）：键=元素名（模板 data-name），值=事件/绑定声明。
    /// 这些键不参与布局渲染，只进入交互图/域索引/实体注册——使模板动态生成的行能声明事件，
    /// 无需为每一行创建 ControlConfig。
    /// </summary>
    [JsonPropertyName("eventTargets")]
    public Dictionary<string, ControlConfig>? EventTargets { get; set; }

    /// <summary>
    /// 页面组件声明（M2/M6）：键=组件名（如 taskRow/historyRow/viewItem），
    /// 值=模板名 + 派生模型规则（白名单算子，见 <c>UiExpression</c>）。
    /// <para>语义：投影器只产基础行模型；派生字段（active/selected/label/elapsed…）由声明计算，
    /// 未声明组件 ⇒ 走 C# 兜底（零回归）。</para>
    /// </summary>
    [JsonPropertyName("components")]
    public Dictionary<string, HtmlComponentDeclaration>? Components { get; set; }

    /// <summary>格式化器别名（M6）：键=表达式使用的名字，值=内置格式化器（duration/number/integer/percent/upper/lower/date）。</summary>
    [JsonPropertyName("formatters")]
    public Dictionary<string, string>? Formatters { get; set; }

    /// <summary>文案表（M6）：表名 → 键 → 文案；供 <c>lookup</c> 算子读取（禁 C# 硬编码 label）。</summary>
    [JsonPropertyName("labels")]
    public Dictionary<string, Dictionary<string, string>>? Labels { get; set; }
}

/// <summary>组件声明（M2/M6）：模板 + 派生模型规则。</summary>
public sealed class HtmlComponentDeclaration
{
    /// <summary>模板名（templates 段键）；null = 用投影器传入的默认模板。</summary>
    [JsonPropertyName("template")]
    public string? Template { get; set; }

    /// <summary>派生模型：键=模型字段，值=白名单算子表达式（JsonElement）。</summary>
    [JsonPropertyName("model")]
    public Dictionary<string, object?>? Model { get; set; }
}

/// <summary>页面受控态槽声明（B2）。</summary>
public sealed class HtmlPageStateSlot
{
    /// <summary>默认值（未回投/首次渲染时生效）；null = 无默认（槽未置位）。</summary>
    [JsonPropertyName("default")]
    public object? Default { get; set; }

    /// <summary>归一化：trim（字符串槽）；缺省/空 = 原样。</summary>
    [JsonPropertyName("normalize")]
    public string? Normalize { get; set; }
}

/// <summary>页面区域声明（B4）。</summary>
public sealed class HtmlPageRegionDeclaration
{
    /// <summary>区域键（写回目标 <c>region:&lt;key&gt;</c>）。</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>区域模板名（templates 段键）；null = 由数据直接投影。</summary>
    [JsonPropertyName("template")]
    public string? Template { get; set; }

    /// <summary>流式段声明（追加写而非替换）；false = 覆盖写。</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}
