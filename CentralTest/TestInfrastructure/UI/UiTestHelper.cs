
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestInfrastructure
{

    public static class UiTestHelper
    {
        internal static T RunOnUi<T>(Func<T> action)
        {
            return MessagePumpEngine.Instance.RunOnUiThread(action);
        }

        internal static void RunOnUi(Action action)
        {
            MessagePumpEngine.Instance.RunOnUiThread(action);
        }


        public static T? FindControl<T>(Control parent, string name) where T : Control
        {
            return RunOnUi(() =>
            {
                var controls = parent.Controls.Find(name, true);
                return controls.FirstOrDefault() as T;
            });
        }

        public static List<T> FindControlsByType<T>(Control parent) where T : Control
        {
            return RunOnUi(() =>
            {
                var result = new List<T>();
                FindControlsRecursive(parent, result);
                return result;
            });
        }

        private static void FindControlsRecursive<T>(Control parent, List<T> result) where T : Control
        {
            foreach (Control control in parent.Controls)
            {
                if (control is T t)
                    result.Add(t);
                if (control.HasChildren)
                    FindControlsRecursive(control, result);
            }
        }

        public static T? FindControlByText<T>(Control parent, string text) where T : Control
        {
            return RunOnUi(() =>
            {
                return FindControlsByType<T>(parent)
                    .FirstOrDefault(c => c.Text == text);
            });
        }

        public static T? WaitForControl<T>(Control parent, string name, int timeoutMs = 5000)
            where T : Control
        {
            T? result = null;
            MessagePumpEngine.Instance.PumpUntil(() =>
            {
                result = FindControl<T>(parent, name);
                return result != null;
            }, timeoutMs);
            return result;
        }


        public static void ClickButton(Button button)
        {
            RunOnUi(() => button.PerformClick());
        }

        public static void SetText(TextBox textBox, string text)
        {
            RunOnUi(() =>
            {
                textBox.Text = text;
                textBox.Select(text.Length, 0);
            });
        }

        public static string GetText(TextBox textBox)
        {
            return RunOnUi(() => textBox.Text);
        }

        public static void SetCheckBox(CheckBox checkBox, bool isChecked)
        {
            RunOnUi(() => checkBox.Checked = isChecked);
        }

        public static bool IsChecked(CheckBox checkBox)
        {
            return RunOnUi(() => checkBox.Checked);
        }

        public static void SelectComboBoxItem(ComboBox comboBox, string item)
        {
            RunOnUi(() => comboBox.SelectedItem = item);
        }

        public static void SelectComboBoxIndex(ComboBox comboBox, int index)
        {
            RunOnUi(() => comboBox.SelectedIndex = index);
        }

        public static string? GetSelectedComboBoxItem(ComboBox comboBox)
        {
            return RunOnUi(() => comboBox.SelectedItem?.ToString());
        }

        public static void SetNumericUpDownValue(NumericUpDown numericUpDown, decimal value)
        {
            RunOnUi(() => numericUpDown.Value = value);
        }

        public static decimal GetNumericUpDownValue(NumericUpDown numericUpDown)
        {
            return RunOnUi(() => numericUpDown.Value);
        }

        public static void SetDateTimePickerValue(DateTimePicker dateTimePicker, DateTime value)
        {
            RunOnUi(() => dateTimePicker.Value = value);
        }

        public static DateTime GetDateTimePickerValue(DateTimePicker dateTimePicker)
        {
            return RunOnUi(() => dateTimePicker.Value);
        }

        public static void SelectRadioButton(RadioButton radioButton)
        {
            RunOnUi(() => radioButton.Checked = true);
        }

        public static bool IsRadioButtonSelected(RadioButton radioButton)
        {
            return RunOnUi(() => radioButton.Checked);
        }

        public static string? GetSelectedListBoxItem(ListBox listBox)
        {
            return RunOnUi(() => listBox.SelectedItem?.ToString());
        }

        public static void SelectListBoxItem(ListBox listBox, string item)
        {
            RunOnUi(() => listBox.SelectedItem = item);
        }

        public static object? GetDataGridViewCellValue(DataGridView grid, int row, int col)
        {
            return RunOnUi(() => grid.Rows[row].Cells[col].Value);
        }

        public static int GetDataGridViewRowCount(DataGridView grid)
        {
            return RunOnUi(() => grid.Rows.Count);
        }

        public static string GetLabelText(Label label)
        {
            return RunOnUi(() => label.Text);
        }

        public static void SetLabelText(Label label, string text)
        {
            RunOnUi(() => label.Text = text);
        }

        public static bool IsVisible(Control control)
        {
            return RunOnUi(() => control.Visible);
        }

        public static bool IsEnabled(Control control)
        {
            return RunOnUi(() => control.Enabled);
        }

        public static Point GetLocation(Control control)
        {
            return RunOnUi(() => control.Location);
        }

        public static Size GetSize(Control control)
        {
            return RunOnUi(() => control.Size);
        }

        public static string GetText(Control control)
        {
            return RunOnUi(() => control.Text);
        }

        public static void SetText(Control control, string text)
        {
            RunOnUi(() => control.Text = text);
        }

        public static void Show(Control control)
        {
            RunOnUi(() => control.Show());
        }

        public static void Hide(Control control)
        {
            RunOnUi(() => control.Hide());
        }

        public static void Enable(Control control)
        {
            RunOnUi(() => control.Enabled = true);
        }

        public static void Disable(Control control)
        {
            RunOnUi(() => control.Enabled = false);
        }

        public static void Focus(Control control)
        {
            RunOnUi(() => control.Focus());
        }

        public static void SendKeys(Control control, string keys)
        {
            RunOnUi(() =>
            {
                control.Focus();
                System.Windows.Forms.SendKeys.Send(keys);
            });
        }

        public static TreeNode? GetSelectedTreeNode(TreeView treeView)
        {
            return RunOnUi(() => treeView.SelectedNode);
        }

        public static void SelectTreeNode(TreeView treeView, string nodePath)
        {
            RunOnUi(() =>
            {
                var parts = nodePath.Split('/');
                TreeNodeCollection nodes = treeView.Nodes;
                TreeNode? currentNode = null;

                foreach (var part in parts)
                {
                    currentNode = nodes.Cast<TreeNode>()
                        .FirstOrDefault(n => n.Text == part);
                    if (currentNode == null) break;
                    nodes = currentNode.Nodes;
                }

                if (currentNode != null)
                {
                    treeView.SelectedNode = currentNode;
                    currentNode.Expand();
                }
            });
        }

        public static ListViewItem? GetSelectedListViewItem(ListView listView)
        {
            return RunOnUi(() =>
            {
                if (listView.SelectedItems.Count > 0)
                    return listView.SelectedItems[0];
                return null;
            });
        }

        public static TabPage? GetSelectedTabPage(TabControl tabControl)
        {
            return RunOnUi(() => tabControl.SelectedTab);
        }

        public static void SelectTabPage(TabControl tabControl, string tabName)
        {
            RunOnUi(() =>
            {
                var tab = tabControl.TabPages.Cast<TabPage>()
                    .FirstOrDefault(t => t.Text == tabName);
                if (tab != null)
                    tabControl.SelectedTab = tab;
            });
        }

        public static int GetProgressBarValue(ProgressBar progressBar)
        {
            return RunOnUi(() => progressBar.Value);
        }

        public static string GetRichTextBoxText(RichTextBox richTextBox)
        {
            return RunOnUi(() => richTextBox.Text);
        }

        public static void SetRichTextBoxText(RichTextBox richTextBox, string text)
        {
            RunOnUi(() => richTextBox.Text = text);
        }

        public static string? GetWebBrowserDocumentText(WebBrowser webBrowser)
        {
            return RunOnUi(() => webBrowser.Document?.Body?.InnerText);
        }

        public static void NavigateWebBrowser(WebBrowser webBrowser, string url)
        {
            RunOnUi(() => webBrowser.Navigate(url));
        }

        public static ToolStripMenuItem? GetMenuItem(MenuStrip menuStrip, string menuPath)
        {
            return RunOnUi(() =>
            {
                var parts = menuPath.Split('/');
                ToolStripItemCollection items = menuStrip.Items;
                ToolStripMenuItem? currentItem = null;

                foreach (var part in parts)
                {
                    currentItem = items.Cast<ToolStripItem>()
                        .OfType<ToolStripMenuItem>()
                        .FirstOrDefault(item => item.Text == part);
                    if (currentItem == null) break;
                    items = currentItem.DropDownItems;
                }

                return currentItem;
            });
        }

        public static void ClickMenuItem(MenuStrip menuStrip, string menuPath)
        {
            var menuItem = GetMenuItem(menuStrip, menuPath);
            if (menuItem != null)
            {
                RunOnUi(() => menuItem.PerformClick());
            }
        }

        public static string? GetStatusLabelText(StatusStrip statusStrip)
        {
            return RunOnUi(() =>
            {
                var label = statusStrip.Items.OfType<ToolStripStatusLabel>().FirstOrDefault();
                return label?.Text;
            });
        }

        public static void SetStatusLabelText(StatusStrip statusStrip, string text)
        {
            RunOnUi(() =>
            {
                var label = statusStrip.Items.OfType<ToolStripStatusLabel>().FirstOrDefault();
                if (label != null)
                    label.Text = text;
            });
        }

        public static ToolStripButton? GetToolStripButton(ToolStrip toolStrip, string buttonText)
        {
            return RunOnUi(() =>
            {
                return toolStrip.Items.OfType<ToolStripButton>()
                    .FirstOrDefault(b => b.Text == buttonText);
            });
        }

        public static void ClickToolStripButton(ToolStrip toolStrip, string buttonText)
        {
            var button = GetToolStripButton(toolStrip, buttonText);
            if (button != null)
            {
                RunOnUi(() => button.PerformClick());
            }
        }

        public static Panel? GetPanel(Control parent, string name)
        {
            return FindControl<Panel>(parent, name);
        }

        public static GroupBox? GetGroupBox(Control parent, string name)
        {
            return FindControl<GroupBox>(parent, name);
        }

        public static SplitContainer? GetSplitContainer(Control parent, string name)
        {
            return FindControl<SplitContainer>(parent, name);
        }

        public static bool IsAutoScroll(Panel panel)
        {
            return RunOnUi(() => panel.AutoScroll);
        }

        public static void SetAutoScroll(Panel panel, bool autoScroll)
        {
            RunOnUi(() => panel.AutoScroll = autoScroll);
        }

        public static Point GetAutoScrollPosition(Panel panel)
        {
            return RunOnUi(() => panel.AutoScrollPosition);
        }

        public static void SetAutoScrollPosition(Panel panel, Point position)
        {
            RunOnUi(() =>
            {
                panel.AutoScrollPosition = new Point(
                    -position.X,
                    -position.Y);
            });
        }
    }


    public class ControlFinder
    {
        private readonly Control _parent;
        private readonly List<Control> _results = new();

        public ControlFinder(Control parent)
        {
            _parent = parent;
        }

        public ControlFinder ByName<T>(string name) where T : Control
        {
            var control = UiTestHelper.FindControl<T>(_parent, name);
            if (control != null)
                _results.Add(control);
            return this;
        }

        public ControlFinder ByType<T>() where T : Control
        {
            _results.AddRange(UiTestHelper.FindControlsByType<T>(_parent));
            return this;
        }

        public ControlFinder ByText<T>(string text) where T : Control
        {
            var control = UiTestHelper.FindControlByText<T>(_parent, text);
            if (control != null)
                _results.Add(control);
            return this;
        }

        public T? FirstOrDefault<T>() where T : Control
        {
            return _results.OfType<T>().FirstOrDefault();
        }

        public List<T> GetAll<T>() where T : Control
        {
            return _results.OfType<T>().ToList();
        }
    }


    public class ControlTester
    {
        private readonly Control _control;
        private readonly List<string> _failures = new();

        public ControlTester(Control control)
        {
            _control = control;
        }

        public ControlTester ShouldBeVisible()
        {
            if (!UiTestHelper.IsVisible(_control))
                _failures.Add($"控件 '{_control.Name}' 应该可见但实际不可见");
            return this;
        }

        public ControlTester ShouldBeHidden()
        {
            if (UiTestHelper.IsVisible(_control))
                _failures.Add($"控件 '{_control.Name}' 应该隐藏但实际可见");
            return this;
        }

        public ControlTester ShouldBeEnabled()
        {
            if (!UiTestHelper.IsEnabled(_control))
                _failures.Add($"控件 '{_control.Name}' 应该启用但实际禁用");
            return this;
        }

        public ControlTester ShouldBeDisabled()
        {
            if (UiTestHelper.IsEnabled(_control))
                _failures.Add($"控件 '{_control.Name}' 应该禁用但实际启用");
            return this;
        }

        public ControlTester ShouldHaveText(string expectedText)
        {
            var actual = UiTestHelper.GetText(_control);
            if (actual != expectedText)
                _failures.Add($"控件 '{_control.Name}' 期望文本 '{expectedText}'，实际 '{actual}'");
            return this;
        }

        public ControlTester ShouldContainText(string partialText)
        {
            var actual = UiTestHelper.GetText(_control);
            if (!actual.Contains(partialText))
                _failures.Add($"控件 '{_control.Name}' 文本应包含 '{partialText}'，实际 '{actual}'");
            return this;
        }

        public ControlTester ShouldHaveName(string expectedName)
        {
            if (_control.Name != expectedName)
                _failures.Add($"控件期望名称 '{expectedName}'，实际 '{_control.Name}'");
            return this;
        }

        public void Verify()
        {
            if (_failures.Count > 0)
            {
                throw new AssertFailedException(
                    $"控件验证失败 ({_failures.Count} 项):\n" +
                    string.Join("\n", _failures.Select(f => $"  - {f}")));
            }
        }
    }
}
