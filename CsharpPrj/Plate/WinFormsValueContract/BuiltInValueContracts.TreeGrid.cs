using System.Collections.Generic;
using System.Windows.Forms;
using TConfig.Controls;

namespace WinFormsValueContract;

internal static partial class BuiltInValueContracts
{
    private static void RegisterTreeViewContract(IDictionary<string, ControlValueContract> target)
    {
        target["treeview"] = Contract(
            "treeview",
            ValueSemantic.Content,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Selection] = ValueAccessor.Create<TreeView>(ReadTreeViewSelection, WriteTreeViewSelection),
                [ValueSemantic.Content] = ValueAccessor.Create<TreeView>(ReadTreeViewContent, WriteTreeViewContent)
            },
            new Dictionary<ValueSemantic, string>
            {
                [ValueSemantic.Selection] = nameof(TreeView.AfterSelect)
            });
    }

    private static object? ReadTreeViewSelection(TreeView tv)
        => tv.SelectedNode?.Tag ?? tv.SelectedNode?.Text;

    private static string? WriteTreeViewSelection(TreeView tv, object? value)
    {
        if (value == null)
        {
            tv.SelectedNode = null;
            return null;
        }

        var node = FindTreeNode(tv.Nodes, value.ToString());
        if (node != null)
        {
            tv.SelectedNode = node;
            return node.Tag?.ToString() ?? node.Text;
        }
        return null;
    }

    private static TreeNode? FindTreeNode(TreeNodeCollection nodes, string? value)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag?.ToString() == value || node.Text == value) return node;
            var found = FindTreeNode(node.Nodes, value);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Content 语义读：回读选中节点规范值（Tag ?? Text），与 Selection 读一致——保证 get/click 取到选中 id。</summary>
    private static object? ReadTreeViewContent(TreeView tv)
        => tv.SelectedNode?.Tag ?? tv.SelectedNode?.Text;

    /// <summary>
    /// Content 语义写：多行 = 目录树文本 → 重建层级节点（文件夹=父，文件=子，Tag=folder/file）；
    /// 单行 = 匹配选中（兼容 set 选中）。默认语义即 Content，initCommand/刷新写入即可重建树。
    /// </summary>
    private static string? WriteTreeViewContent(TreeView tv, object? value)
    {
        var s = value?.ToString();
        if (string.IsNullOrEmpty(s))
        {
            tv.Nodes.Clear();
            tv.SelectedNode = null;
            return null;
        }

        if (s.IndexOf('\n') >= 0)
        {
            BuildTreeNodes(tv, s);
            return null;
        }

        return WriteTreeViewSelection(tv, s);
    }

    /// <summary>按目录树文本重建 TreeView 节点：文件夹行（无 /）为父节点，文件行（folder/file）为子节点（Text=文件，Tag=完整 id）。</summary>
    private static void BuildTreeNodes(TreeView tv, string treeText)
    {
        tv.BeginUpdate();
        try
        {
            tv.Nodes.Clear();
            tv.SelectedNode = null;
            var folderNodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in treeText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var slash = line.IndexOf('/');
                if (slash < 0)
                {
                    var folderNode = new TreeNode(line) { Tag = null };
                    tv.Nodes.Add(folderNode);
                    folderNodes[line] = folderNode;
                }
                else
                {
                    var folder = line.Substring(0, slash);
                    var file = line.Substring(slash + 1);
                    if (!folderNodes.TryGetValue(folder, out var folderNode))
                    {
                        folderNode = new TreeNode(folder) { Tag = null };
                        tv.Nodes.Add(folderNode);
                        folderNodes[folder] = folderNode;
                    }
                    folderNode.Nodes.Add(new TreeNode(file) { Tag = line });
                }
            }
        }
        finally
        {
            tv.EndUpdate();
        }

        // 建节点后强制重绘，避免“数据在、看不见”。
        tv.Refresh();
    }

    private static void RegisterDataGridViewContract(IDictionary<string, ControlValueContract> target)
    {
        target["datagridview"] = Contract(
            "datagridview",
            ValueSemantic.Selection,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Selection] = ValueAccessor.Create<DataGridView>(ReadDataGridViewSelection, WriteDataGridViewSelection)
            },
            new Dictionary<ValueSemantic, string>
            {
                [ValueSemantic.Selection] = nameof(DataGridView.SelectionChanged)
            });
    }

    private static object? ReadDataGridViewSelection(DataGridView dgv)
    {
        if (dgv.CurrentRow != null && !dgv.CurrentRow.IsNewRow)
            return dgv.CurrentRow.DataBoundItem?.ToString();
        return null;
    }

    private static string? WriteDataGridViewSelection(DataGridView dgv, object? value)
    {
        if (value == null)
        {
            dgv.ClearSelection();
            return null;
        }

        if (dgv.DataSource != null)
        {
            var str = value.ToString();
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.DataBoundItem?.ToString() == str)
                {
                    row.Selected = true;
                    dgv.CurrentCell = row.Cells[0];
                    return row.DataBoundItem?.ToString();
                }
            }
        }
        return null;
    }
}
