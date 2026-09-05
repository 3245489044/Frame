#nullable enable

using System;
using System.Windows.Forms;
using Plate.HtmlViewer;

namespace FrontEndDriven.Shared;

/// <summary>
/// 事件订阅统一分派（D6 收敛 + E1 修复）：事件名（大小写不敏感）→ 控件物理事件订阅的唯一实现。
/// checkedchanged/selectedindexchanged/valuechanged 等跨类型事件按控件实际类型分派，
/// 消除 EventBindingSystem 与 InputCapturePort 各自 switch 的重复与实现漂移
/// （InputCapturePort 曾对 RadioButton 强制转 CheckBox 抛 InvalidCastException）。
/// </summary>
public static class EventSubscriptionHelper
{
    /// <summary>
    /// 可物理订阅事件名集合（E5 单一真源）——<see cref="TrySubscribe"/> 实际支持的降序集合。
    /// 与 <see cref="EventHelper.StandardEventTypes"/>（标准事件名词典，约 50 个，另用于 PascalCase 映射）区分：
    /// 后者含大量标准 WinForms 事件但本分派器未实现物理挂接。新增可物理订阅事件须同时在此登记并实现分派，
    /// 校验器（EventBindingValidator）据本集合对"标准词表有、能力集无"的事件发出明确告警。
    /// </summary>
    public static readonly HashSet<string> PhysicallySubscribableEvents = new(
        new[]
        {
            "click", "doubleclick", "textchanged", "checkedchanged",
            "selectedindexchanged", "valuechanged", "afterselect", "selectionchanged",
            "message"
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按事件名订阅控件物理事件（<paramref name="onRaised"/> 为事件触发回调）。
    /// 返回 true=已成功订阅；false=事件名不支持或控件类型不匹配（由调用方决定记录/跳过语义）。
    /// </summary>
    public static bool TrySubscribe(Control control, string eventName, Action onRaised)
    {
        if (control == null || string.IsNullOrWhiteSpace(eventName) || onRaised == null)
            return false;

        switch (eventName.ToLowerInvariant())
        {
            case "click":
                control.Click += (_, _) => onRaised();
                return true;

            case "doubleclick":
                control.DoubleClick += (_, _) => onRaised();
                return true;

            case "textchanged":
                control.TextChanged += (_, _) => onRaised();
                return true;

            case "checkedchanged":
                // CheckBox / RadioButton 共享 CheckedChanged 事件名（RadioButton 非 CheckBox 派生，须分别匹配）
                switch (control)
                {
                    case CheckBox checkBox:
                        checkBox.CheckedChanged += (_, _) => onRaised();
                        return true;
                    case RadioButton radioButton:
                        radioButton.CheckedChanged += (_, _) => onRaised();
                        return true;
                }
                break;

            case "selectedindexchanged":
                // CheckedListBox 继承 ListBox，须先子类后基类
                switch (control)
                {
                    case ListView listView:
                        listView.SelectedIndexChanged += (_, _) => onRaised();
                        return true;
                    case ComboBox comboBox:
                        comboBox.SelectedIndexChanged += (_, _) => onRaised();
                        return true;
                    case CheckedListBox checkedListBox:
                        checkedListBox.SelectedIndexChanged += (_, _) => onRaised();
                        return true;
                    case ListBox listBox:
                        listBox.SelectedIndexChanged += (_, _) => onRaised();
                        return true;
                }
                break;

            case "valuechanged":
                switch (control)
                {
                    case NumericUpDown numericUpDown:
                        numericUpDown.ValueChanged += (_, _) => onRaised();
                        return true;
                    case TrackBar trackBar:
                        trackBar.ValueChanged += (_, _) => onRaised();
                        return true;
                    case DateTimePicker dateTimePicker:
                        dateTimePicker.ValueChanged += (_, _) => onRaised();
                        return true;
                }
                break;

            case "afterselect":
                if (control is TreeView treeView)
                {
                    treeView.AfterSelect += (_, _) => onRaised();
                    return true;
                }
                break;

            case "selectionchanged":
                if (control is DataGridView dataGridView)
                {
                    dataGridView.SelectionChanged += (_, _) => onRaised();
                    return true;
                }
                break;

            case "message":
                // htmlviewer：HTML 侧 [data-cmd] 元素点击 → postMessage → MessageReceived（负载存于 LastMessagePayloadJson，参数绑定经 eventPayload 源读取）。
                if (control is HtmlViewer htmlViewer)
                {
                    htmlViewer.MessageReceived += (_, _) => onRaised();
                    return true;
                }
                break;
        }

        return false;
    }
}
