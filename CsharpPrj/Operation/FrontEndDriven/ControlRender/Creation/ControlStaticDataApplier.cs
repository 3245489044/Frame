#nullable enable

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using TConfig.ECS.Components;
using FrontEndDriven.Diagnostics;

namespace FrontEndDriven.ECS;

/// <summary>
/// 静态数据应用器：把 ControlStaticDataComponent（ControlFactory 从 ControlConfig 捕获的构造期静态数据）
/// 按控件类型应用物理控件。与 ControlStrategyRegistry（创建工厂）平行、与 ApplyControlBehavior（行为）互补——
/// 统一在 WinFormsOutputPort.CreateControl 创建期单点应用，不做快照差异重复应用（避免双写）。
/// 覆盖 schema 已声明但旧管线整体丢弃的静态属性（items/selectedIndex/checkedItems/columns/view/checked/dropDownStyle）。
/// </summary>
public static class ControlStaticDataApplier
{
    /// <summary>已覆盖静态数据应用的控件类型集合（声明即覆盖守卫：有静态数据但无应用器的类型 → 显式告警，不再静默丢弃）。</summary>
    private static readonly HashSet<string> CoveredTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "combobox", "listbox", "checkedlistbox", "domainupdown", "listview", "checkbox"
    };

    public static bool HasApplier(string controlType)
        => !string.IsNullOrWhiteSpace(controlType) && CoveredTypes.Contains(controlType);

    public static void Apply(string controlType, Control control, ControlStaticDataComponent data)
    {
        switch (controlType.ToLowerInvariant())
        {
            case "combobox":
                ApplyComboBox(control as ComboBox, data);
                break;
            case "listbox":
                ApplyListBox(control as ListBox, data);
                break;
            case "checkedlistbox":
                ApplyCheckedListBox(control as CheckedListBox, data);
                break;
            case "domainupdown":
                ApplyDomainUpDown(control as DomainUpDown, data);
                break;
            case "listview":
                ApplyListView(control as ListView, data);
                break;
            case "checkbox":
                ApplyCheckBox(control as CheckBox, data);
                break;
        }
    }

    private static void ApplyComboBox(ComboBox? comboBox, ControlStaticDataComponent data)
    {
        if (comboBox == null) return;

        if (!string.IsNullOrWhiteSpace(data.DropDownStyle)
            && Enum.TryParse<ComboBoxStyle>(data.DropDownStyle, true, out var style))
        {
            comboBox.DropDownStyle = style;
        }

        if (data.Items is { Count: > 0 })
        {
            comboBox.Items.Clear();
            comboBox.Items.AddRange(data.Items.ToArray());
        }

        if (data.SelectedIndex is int idx && idx >= 0 && idx < comboBox.Items.Count)
            comboBox.SelectedIndex = idx;
    }

    private static void ApplyListBox(ListBox? listBox, ControlStaticDataComponent data)
    {
        if (listBox == null) return;

        if (data.Items is { Count: > 0 })
        {
            listBox.Items.Clear();
            listBox.Items.AddRange(data.Items.ToArray());
        }

        if (data.SelectedIndex is int idx && idx >= 0 && idx < listBox.Items.Count)
            listBox.SelectedIndex = idx;
    }

    private static void ApplyCheckedListBox(CheckedListBox? checkedListBox, ControlStaticDataComponent data)
    {
        if (checkedListBox == null) return;

        if (data.Items is { Count: > 0 })
        {
            checkedListBox.Items.Clear();
            checkedListBox.Items.AddRange(data.Items.ToArray());
        }

        if (data.CheckedItems is { Count: > 0 })
        {
            for (int i = 0; i < checkedListBox.Items.Count; i++)
            {
                if (data.CheckedItems.Contains(checkedListBox.Items[i]?.ToString()))
                    checkedListBox.SetItemChecked(i, true);
            }
        }
    }

    private static void ApplyDomainUpDown(DomainUpDown? domainUpDown, ControlStaticDataComponent data)
    {
        if (domainUpDown == null) return;

        if (data.Items is { Count: > 0 })
        {
            domainUpDown.Items.Clear();
            domainUpDown.Items.AddRange(data.Items.ToArray());
        }

        if (data.SelectedIndex is int idx && idx >= 0 && idx < domainUpDown.Items.Count)
            domainUpDown.SelectedIndex = idx;
    }

    private static void ApplyListView(ListView? listView, ControlStaticDataComponent data)
    {
        if (listView == null) return;

        if (!string.IsNullOrWhiteSpace(data.View)
            && Enum.TryParse<View>(data.View, true, out var view))
        {
            listView.View = view;
        }

        listView.FullRowSelect = data.FullRowSelect;
        listView.GridLines = data.GridLines;

        if (data.Columns is { Count: > 0 })
        {
            listView.Columns.Clear();
            foreach (var col in data.Columns)
            {
                listView.Columns.Add(new ColumnHeader
                {
                    Text = col.Text ?? col.Name,
                    Width = col.Width > 0 ? col.Width : 100
                });
            }
        }
    }

    private static void ApplyCheckBox(CheckBox? checkBox, ControlStaticDataComponent data)
    {
        if (checkBox == null) return;

        if (data.Checked is bool chk)
            checkBox.Checked = chk;
    }
}
