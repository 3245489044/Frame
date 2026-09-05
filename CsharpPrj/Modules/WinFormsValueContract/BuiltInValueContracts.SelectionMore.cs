using System.Windows.Forms;
using TConfig.Controls;

namespace WinFormsValueContract;

internal static partial class BuiltInValueContracts
{
    private static void RegisterListBoxContract(IDictionary<string, ControlValueContract> target)
    {
        target["listbox"] = Contract(
            "listbox",
            ValueSemantic.Selection,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Selection] = ValueAccessor.Create<ListBox>(ReadListBoxSelection, WriteListBoxSelection),
                [ValueSemantic.Content] = ValueAccessor.Create(
                    c => string.Join("\n", ((ListBox)c).Items.Cast<object>().Select(i => i.ToString())),
                    (c, v) =>
                    {
                        var lb = (ListBox)c;
                        lb.BeginUpdate();
                        try
                        {
                            lb.Items.Clear();
                            foreach (var line in SplitLines(v)) lb.Items.Add(line);
                        }
                        finally { lb.EndUpdate(); }
                    })
            },
            new Dictionary<ValueSemantic, string>
            {
                [ValueSemantic.Selection] = nameof(ListBox.SelectedIndexChanged)
            });
    }

    private static object? ReadListBoxSelection(ListBox lb)
        => lb.SelectedValue ?? lb.SelectedItem;

    private static string? WriteListBoxSelection(ListBox lb, object? value)
    {
        if (value == null)
        {
            lb.ClearSelected();
            return null;
        }

        var str = value.ToString();
        foreach (var item in lb.Items)
        {
            if (string.Equals(item?.ToString(), str, StringComparison.OrdinalIgnoreCase))
            {
                lb.SelectedItem = item;
                return item?.ToString();
            }
        }
        return null;
    }

    private static void RegisterCheckedListBoxContract(IDictionary<string, ControlValueContract> target)
    {
        target["checkedlistbox"] = Contract(
            "checkedlistbox",
            ValueSemantic.Selection,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Selection] = ValueAccessor.Create<CheckedListBox>(ReadCheckedListBoxSelection, WriteCheckedListBoxSelection)
            },
            new Dictionary<ValueSemantic, string>
            {
                [ValueSemantic.Selection] = nameof(CheckedListBox.SelectedIndexChanged)
            });
    }

    private static object? ReadCheckedListBoxSelection(CheckedListBox clb)
        => clb.CheckedItems.Count > 0 ? clb.CheckedItems[0] : null;

    private static string? WriteCheckedListBoxSelection(CheckedListBox clb, object? value)
    {
        if (value == null)
        {
            for (var i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false);
            return null;
        }

        var str = value.ToString();
        for (var i = 0; i < clb.Items.Count; i++)
        {
            if (string.Equals(clb.Items[i]?.ToString(), str, StringComparison.OrdinalIgnoreCase))
            {
                clb.SetItemChecked(i, true);
                return clb.Items[i]?.ToString();
            }
        }
        return null;
    }
}
