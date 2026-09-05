using System.Collections.Generic;

namespace TConfig.ECS.Components;

/// <summary>
/// 控件静态数据组件（Selection/Collection 控件的构造期数据事实：items/selectedIndex/checkedItems/columns/…）。
/// 与 <see cref="ControlBehaviorComponent"/> 同构：由 <c>ControlFactory.CreateFromConfig</c> 从 <c>ControlConfig</c>
/// 写入（单一权威），WinFormsOutputPort.CreateControl 经 ControlStaticDataApplier 创建期应用物理控件。
/// 属「构造期静态数据」，非运行值——与值语义（ControlValueComponent：选中值）分离，不混用：
/// 本组件回答「下拉里有哪些项」，值组件回答「当前选中哪一项」。
/// 覆盖 schema 已声明但旧管线整体丢弃的静态属性（items/selectedIndex/checkedItems/columns/view/checked/dropDownStyle）。
/// </summary>
public struct ControlStaticDataComponent : IComponent
{
    /// <summary>下拉/列表项（combobox/listbox/checkedlistbox/domainupdown 等）。</summary>
    public List<string>? Items;

    /// <summary>默认选中索引（combobox/listbox）。TabControl 选中态由 TabContainerComponent 承载，不在此列。</summary>
    public int? SelectedIndex;

    /// <summary>预勾选项（checkedlistbox）。</summary>
    public List<string>? CheckedItems;

    /// <summary>列头（listview）。</summary>
    public List<ListViewColumnConfig>? Columns;

    /// <summary>listview 视图（Details/LargeIcon/SmallIcon/List/Tile）。</summary>
    public string? View;

    /// <summary>listview 整行选中。</summary>
    public bool FullRowSelect;

    /// <summary>listview 网格线。</summary>
    public bool GridLines;

    /// <summary>combobox 下拉样式（DropDown/DropDownList/Simple），缺省 DropDown。</summary>
    public string? DropDownStyle;

    /// <summary>checkbox 勾选态。</summary>
    public bool? Checked;
}
