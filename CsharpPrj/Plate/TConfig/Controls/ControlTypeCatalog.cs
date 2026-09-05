using System.Collections.Generic;

namespace TConfig.Controls;

/// <summary>
/// 控件类型目录（阶段C·语义层汇聚）：UI 无关的控件类型词表——受支持的控件类型键 + 容器标志 + 默认值语义。
/// 与 <see cref="ValueSemantic"/>、<see cref="ControlValueContract"/> 同属共享语义层（TConfig.Controls），
/// 是各后端控件实现（ControlTypeRegistry 工厂、ValueContractRegistry 值契约、ControlStrategyRegistry 策略）共同的
/// 类型词表唯一真源。新增控件类型 = 在此登记一条目录 + 在后端注册对应实现，消费方零改动。
/// </summary>
public sealed record ControlTypeInfo(
    string TypeName,
    bool IsContainer = false,
    ValueSemantic? DefaultSemantic = null);

/// <summary>
/// 控件类型目录注册表：按类型名（OrdinalIgnoreCase）查询。数据纯（字符串+枚举），零 UI 工具包依赖。
/// </summary>
public static class ControlTypeCatalog
{
    private static readonly Dictionary<string, ControlTypeInfo> _catalog = new(StringComparer.OrdinalIgnoreCase);

    static ControlTypeCatalog()
    {
        // ── 简单文本控件 ──
        Register("textbox", defaultSemantic: ValueSemantic.Text);
        Register("richtextbox", defaultSemantic: ValueSemantic.Text);
        Register("maskedtextbox", defaultSemantic: ValueSemantic.Text);
        Register("label", defaultSemantic: ValueSemantic.Text);
        Register("linklabel", defaultSemantic: ValueSemantic.Text);
        Register("button", defaultSemantic: ValueSemantic.Text);
        Register("domainupdown", defaultSemantic: ValueSemantic.Text);
        Register("datetimepicker", defaultSemantic: ValueSemantic.Text);

        // ── 布尔 / 范围 / 路径 ──
        Register("checkbox", defaultSemantic: ValueSemantic.Checked);
        Register("radiobutton", defaultSemantic: ValueSemantic.Checked);
        Register("numericupdown", defaultSemantic: ValueSemantic.Range);
        Register("trackbar", defaultSemantic: ValueSemantic.Range);
        Register("progressbar", defaultSemantic: ValueSemantic.Range);
        Register("picturebox", defaultSemantic: ValueSemantic.Path);

        // ── 选择 / 内容 ──
        Register("listview", defaultSemantic: ValueSemantic.Selection);
        Register("combobox", defaultSemantic: ValueSemantic.Selection);
        Register("listbox", defaultSemantic: ValueSemantic.Selection);
        Register("checkedlistbox", defaultSemantic: ValueSemantic.Selection);
        Register("treeview", defaultSemantic: ValueSemantic.Content);
        Register("datagridview", defaultSemantic: ValueSemantic.Selection);

        // ── 无值语义（仅渲染 / 导航） ──
        Register("webbrowser");
        Register("markdownviewer");
        Register("htmlviewer");

        Register("statusstrip");
        Register("toolstrip");
        Register("menustrip");
        Register("contextmenustrip");

        // ── 容器控件 ──
        Register("groupbox", isContainer: true, defaultSemantic: ValueSemantic.Text);
        Register("flowlayoutpanel", isContainer: true);
        Register("tablelayoutpanel", isContainer: true);
        Register("tabcontrol", isContainer: true);
        Register("tabpage", isContainer: true);
        Register("container", isContainer: true);
        Register("panel", isContainer: true);
        Register("form", isContainer: true);
        Register("splitcontainer", isContainer: true);
        Register("splitterpanel", isContainer: true);
    }

    private static void Register(string typeName, bool isContainer = false, ValueSemantic? defaultSemantic = null)
        => _catalog[typeName] = new ControlTypeInfo(typeName, isContainer, defaultSemantic);

    /// <summary>是否已知控件类型。</summary>
    public static bool Contains(string typeName)
        => !string.IsNullOrWhiteSpace(typeName) && _catalog.ContainsKey(typeName);

    /// <summary>控件类型信息；未知返回 null。</summary>
    public static ControlTypeInfo? Get(string typeName)
        => !string.IsNullOrWhiteSpace(typeName) && _catalog.TryGetValue(typeName, out var info) ? info : null;

    /// <summary>是否容器类型。</summary>
    public static bool IsContainer(string typeName)
        => Get(typeName)?.IsContainer == true;

    /// <summary>类型默认值语义；未知或未声明返回 null。</summary>
    public static ValueSemantic? GetDefaultSemantic(string typeName)
        => Get(typeName)?.DefaultSemantic;

    /// <summary>全部目录条目（注册顺序稳定）。</summary>
    public static IReadOnlyCollection<ControlTypeInfo> All => _catalog.Values;
}
