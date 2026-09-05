using System.Windows.Forms;
using TConfig.Controls;

namespace WinFormsValueContract;

internal static partial class BuiltInValueContracts
{
    // ── Selection 语义（读选中值 / 写匹配选中 / 写后回读规范值）──

    private static void RegisterSelectionContracts(IDictionary<string, ControlValueContract> target)
    {
        RegisterListViewContract(target);
        RegisterComboBoxContract(target);
        RegisterListBoxContract(target);
        RegisterCheckedListBoxContract(target);
        RegisterTreeViewContract(target);
        RegisterDataGridViewContract(target);
        RegisterTabControlContract(target);
    }

    private static void RegisterListViewContract(IDictionary<string, ControlValueContract> target)
    {
        target["listview"] = Contract(
            "listview",
            ValueSemantic.Selection,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Selection] = ValueAccessor.Create<ListView>(ReadListViewSelection, WriteListViewSelection),
                [ValueSemantic.Content] = ValueAccessor.Create<ListView>(ReadListViewContent, WriteListViewContent)
            },
            new Dictionary<ValueSemantic, string>
            {
                [ValueSemantic.Selection] = nameof(ListView.SelectedIndexChanged)
            });
    }

    private static object? ReadListViewSelection(ListView lv)
        => lv.SelectedItems.Count == 0 ? null : lv.SelectedItems[0].Tag ?? lv.SelectedItems[0].Text;

    /// <summary>
    /// 按值匹配选中项（单选语义：先清空选中再匹配，Tag/Text 包含匹配，与脚本 set 兼容），
    /// 返回选中项规范值（Tag ?? Text）；无匹配不改选中。
    /// 修复：原实现"只设选不清"导致多选时 SelectedItems[0] 为"先选中的项"（如列表加载默认选中的首项），
    /// property 物理读（SelectedItems.0.Tag）读到错误项（AgentConfigUI 误替 MathFunctions 缺陷根因）。
    /// </summary>
    private static string? WriteListViewSelection(ListView lv, object? value)
    {
        if (value == null)
        {
            lv.SelectedItems.Clear();
            return null;
        }

        var str = value.ToString();

        // 多行值 = 列表内容（输出/刷新填充）：重建项并清空选中。
        // 默认语义是 Selection（读=选中项，保证双击取选中模板/参数读选中项），但写多行列表时下沉为"填充项"，
        // 使刷新/列表输出能填充物理 ListView，而双击/参数读仍走 Selection 读选中项。
        if (str.IndexOf('\n') >= 0)
        {
            lv.BeginUpdate();
            try
            {
                lv.Items.Clear();
                lv.SelectedItems.Clear();
                foreach (var line in SplitLines(value)) lv.Items.Add(new ListViewItem(line));
            }
            finally { lv.EndUpdate(); }
            return null;
        }

        // 单行 = 匹配选中（单选语义：先清空选中再匹配，保证 SelectedItems[0] 唯一且为本次匹配项）
        foreach (ListViewItem item in lv.Items)
            item.Selected = false;

        foreach (ListViewItem item in lv.Items)
        {
            if (item.Tag?.ToString()?.Contains(str, StringComparison.OrdinalIgnoreCase) == true
                || item.Text?.Contains(str, StringComparison.OrdinalIgnoreCase) == true)
            {
                item.Selected = true;
                return item.Tag?.ToString() ?? item.Text;
            }
        }

        return null;
    }

    private static object? ReadListViewContent(ListView lv)
    {
        var items = new List<string>();
        foreach (ListViewItem item in lv.Items) items.Add(item.Text);
        return string.Join("\n", items);
    }

    /// <summary>
    /// Content 语义写（显式指定时；默认语义为 Selection，其写路径同样处理多行填充/单行选中）：多行 = 重建列表项；
    /// 单行 = 匹配选中（返回规范值 Tag??Text）。空值清空项与选中。
    /// </summary>
    private static string? WriteListViewContent(ListView lv, object? value)
    {
        var s = value?.ToString();
        if (string.IsNullOrEmpty(s))
        {
            lv.Items.Clear();
            lv.SelectedItems.Clear();
            return null;
        }

        if (s.IndexOf('\n') >= 0)
        {
            lv.BeginUpdate();
            try
            {
                lv.Items.Clear();
                lv.SelectedItems.Clear();
                foreach (var line in SplitLines(value)) lv.Items.Add(new ListViewItem(line));
            }
            finally { lv.EndUpdate(); }
            return null;
        }

        return WriteListViewSelection(lv, s);
    }

    private static void RegisterComboBoxContract(IDictionary<string, ControlValueContract> target)
    {
        target["combobox"] = Contract(
            "combobox",
            ValueSemantic.Selection,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Selection] = ValueAccessor.Create<ComboBox>(ReadComboBoxSelection, WriteComboBoxSelection),
                [ValueSemantic.Content] = ValueAccessor.Create(
                    c => string.Join("\n", ((ComboBox)c).Items.Cast<object>().Select(i => i.ToString())),
                    (c, v) =>
                    {
                        var cb = (ComboBox)c;
                        cb.BeginUpdate();
                        try
                        {
                            cb.Items.Clear();
                            foreach (var line in SplitLines(v)) cb.Items.Add(line);
                        }
                        finally { cb.EndUpdate(); }
                    })
            },
            new Dictionary<ValueSemantic, string>
            {
                [ValueSemantic.Selection] = nameof(ComboBox.SelectedIndexChanged)
            });
    }

    private static object? ReadComboBoxSelection(ComboBox cb)
        => cb.SelectedValue ?? cb.SelectedItem ?? cb.Text;

    private static string? WriteComboBoxSelection(ComboBox cb, object? value)
    {
        if (value == null)
        {
            cb.SelectedIndex = -1;
            return null;
        }

        var str = value.ToString();
        foreach (var item in cb.Items)
        {
            if (string.Equals(item?.ToString(), str, StringComparison.OrdinalIgnoreCase))
            {
                cb.SelectedItem = item;
                return item?.ToString();
            }
        }

        if (int.TryParse(str, out var index) && index >= 0 && index < cb.Items.Count)
        {
            cb.SelectedIndex = index;
            return cb.SelectedItem?.ToString();
        }

        return null;
    }

    // ── TabControl 语义（读选中页名 / 写按 Name|Text|索引匹配选中页）──

    private static void RegisterTabControlContract(IDictionary<string, ControlValueContract> target)
    {
        target["tabcontrol"] = Contract(
            "tabcontrol",
            ValueSemantic.Selection,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Selection] = ValueAccessor.Create<TabControl>(ReadTabControlSelection, WriteTabControlSelection)
            },
            new Dictionary<ValueSemantic, string>
            {
                [ValueSemantic.Selection] = nameof(TabControl.SelectedIndexChanged)
            });
    }

    private static object? ReadTabControlSelection(TabControl tc)
        => tc.SelectedTab?.Name ?? tc.SelectedTab?.Text;

    /// <summary>按值选中页：先按 Name 精确，再按 Text 精确，最后按数字索引；无匹配不改选中。</summary>
    private static string? WriteTabControlSelection(TabControl tc, object? value)
    {
        if (value == null) return null;
        var str = value.ToString();

        foreach (TabPage page in tc.TabPages)
        {
            if (string.Equals(page.Name, str, StringComparison.OrdinalIgnoreCase)
                || string.Equals(page.Text, str, StringComparison.OrdinalIgnoreCase))
            {
                tc.SelectedTab = page;
                return page.Name ?? page.Text;
            }
        }

        if (int.TryParse(str, out var idx) && idx >= 0 && idx < tc.TabPages.Count)
        {
            tc.SelectedTab = tc.TabPages[idx];
            return tc.TabPages[idx].Name ?? tc.TabPages[idx].Text;
        }

        return null;
    }
}
