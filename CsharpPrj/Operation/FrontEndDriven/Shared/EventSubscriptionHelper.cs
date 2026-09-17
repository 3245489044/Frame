#nullable enable

using System;
using System.Windows.Forms;
using Modules.Pipeline;
using Plate.HtmlViewer;

namespace FrontEndDriven.Shared;

/// <summary>
/// 事件订阅统一分派（D6 收敛 + E1 修复；Q2-4 管线化）：事件名（大小写不敏感）→ 控件物理事件订阅的唯一实现。
/// <para>checkedchanged/selectedindexchanged/valuechanged 等跨类型事件按控件实际类型分派，
/// 消除 EventBindingSystem 与 InputCapturePort 各自 switch 的重复与实现漂移
/// （InputCapturePort 曾对 RadioButton 强制转 CheckBox 抛 InvalidCastException）。</para>
/// <para>分派机制：<c>PipelineSwitch</c>（键 = 事件名）+ <c>AsBranch</c> 分支表——**表即真源**：
/// <see cref="PhysicallySubscribableEvents"/> 由表键派生，新增可订阅事件只改表一处（分支体 = 迁移前 case 体，逐字一致）。</para>
/// </summary>
public static class EventSubscriptionHelper
{
    /// <summary>
    /// 事件名 → 物理订阅分支（**唯一真源**）：键 = 小写事件名；分支体负责订阅并把
    /// <see cref="SubscriptionContext.Subscribed"/> 置位。
    /// </summary>
    private static readonly Dictionary<string, IPipeline<SubscriptionContext, SubscriptionContext>> Branches =
        BuildBranches();

    /// <summary>
    /// 分派器（键 = 事件名；缺省分支 = 无操作 → 返回 false，与迁移前「switch 无匹配」同语义）。
    /// </summary>
    private static readonly PipelineSwitch<SubscriptionContext, string> Dispatcher = new(
        context => context.EventName,
        Branches,
        defaultBranch: Branch(_ => { }),
        name: "EventSubscriptionDispatch",
        description: "事件名 → 控件物理事件订阅（Q2-4）");

    /// <summary>
    /// 可物理订阅事件名集合（E5 单一真源）——**由 <see cref="Branches"/> 键集派生**（禁再手抄一份：
    /// 迁移前本集合与 switch 分派双处维护，漂移即校验器误报/漏报）。
    /// 与 <see cref="EventHelper.StandardEventTypes"/>（标准事件名词典，约 50 个，另用于 PascalCase 映射）区分：
    /// 后者含大量标准 WinForms 事件但本分派器未实现物理挂接。校验器（EventBindingValidator）据本集合对
    /// "标准词表有、能力集无"的事件发出明确告警。
    /// </summary>
    public static readonly HashSet<string> PhysicallySubscribableEvents = new(
        Branches.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按事件名订阅控件物理事件（<paramref name="onRaised"/> 为事件触发回调）。
    /// 返回 true=已成功订阅；false=事件名不支持或控件类型不匹配（由调用方决定记录/跳过语义）。
    /// </summary>
    public static bool TrySubscribe(Control control, string eventName, Action onRaised)
    {
        if (control == null || string.IsNullOrWhiteSpace(eventName) || onRaised == null)
            return false;

        var context = new SubscriptionContext(control, eventName.ToLowerInvariant(), onRaised);
        Dispatcher.Execute(context);
        return context.Subscribed;
    }

    /// <summary>分支包装（机制单源 = 框架 <c>AsBranch</c>）。</summary>
    private static IPipeline<SubscriptionContext, SubscriptionContext> Branch(Action<SubscriptionContext> body)
        => body.AsBranch("SubscribeBranch");

    /// <summary>订阅分派上下文：控件 + 事件名（已小写）+ 回调 + 订阅结果位。</summary>
    private sealed class SubscriptionContext(Control control, string eventName, Action onRaised)
    {
        internal Control Control { get; } = control;

        internal string EventName { get; } = eventName;

        internal Action OnRaised { get; } = onRaised;

        /// <summary>是否已完成物理订阅（分支置位；false = 事件不支持 / 控件类型不匹配）。</summary>
        internal bool Subscribed { get; set; }
    }

    /// <summary>分支表（事件名 → 订阅动作；分支体 = 迁移前 <c>case</c> 体，逐字一致）。</summary>
    private static Dictionary<string, IPipeline<SubscriptionContext, SubscriptionContext>> BuildBranches()
        => new(StringComparer.Ordinal)
        {
            ["click"] = Branch(context =>
            {
                context.Control.Click += (_, _) => context.OnRaised();
                context.Subscribed = true;
            }),

            ["doubleclick"] = Branch(context =>
            {
                context.Control.DoubleClick += (_, _) => context.OnRaised();
                context.Subscribed = true;
            }),

            ["textchanged"] = Branch(context =>
            {
                context.Control.TextChanged += (_, _) => context.OnRaised();
                context.Subscribed = true;
            }),

            ["checkedchanged"] = Branch(context =>
            {
                // CheckBox / RadioButton 共享 CheckedChanged 事件名（RadioButton 非 CheckBox 派生，须分别匹配）
                switch (context.Control)
                {
                    case CheckBox checkBox:
                        checkBox.CheckedChanged += (_, _) => context.OnRaised();
                        context.Subscribed = true;
                        return;
                    case RadioButton radioButton:
                        radioButton.CheckedChanged += (_, _) => context.OnRaised();
                        context.Subscribed = true;
                        return;
                }
            }),

            ["selectedindexchanged"] = Branch(context =>
            {
                // CheckedListBox 继承 ListBox，须先子类后基类
                switch (context.Control)
                {
                    case ListView listView:
                        listView.SelectedIndexChanged += (_, _) => context.OnRaised();
                        context.Subscribed = true;
                        return;
                    case ComboBox comboBox:
                        comboBox.SelectedIndexChanged += (_, _) => context.OnRaised();
                        context.Subscribed = true;
                        return;
                    case CheckedListBox checkedListBox:
                        checkedListBox.SelectedIndexChanged += (_, _) => context.OnRaised();
                        context.Subscribed = true;
                        return;
                    case ListBox listBox:
                        listBox.SelectedIndexChanged += (_, _) => context.OnRaised();
                        context.Subscribed = true;
                        return;
                }
            }),

            ["valuechanged"] = Branch(context =>
            {
                switch (context.Control)
                {
                    case NumericUpDown numericUpDown:
                        numericUpDown.ValueChanged += (_, _) => context.OnRaised();
                        context.Subscribed = true;
                        return;
                    case TrackBar trackBar:
                        trackBar.ValueChanged += (_, _) => context.OnRaised();
                        context.Subscribed = true;
                        return;
                    case DateTimePicker dateTimePicker:
                        dateTimePicker.ValueChanged += (_, _) => context.OnRaised();
                        context.Subscribed = true;
                        return;
                }
            }),

            ["afterselect"] = Branch(context =>
            {
                if (context.Control is TreeView treeView)
                {
                    treeView.AfterSelect += (_, _) => context.OnRaised();
                    context.Subscribed = true;
                }
            }),

            ["selectionchanged"] = Branch(context =>
            {
                if (context.Control is DataGridView dataGridView)
                {
                    dataGridView.SelectionChanged += (_, _) => context.OnRaised();
                    context.Subscribed = true;
                }
            }),

            ["message"] = Branch(context =>
            {
                // htmlviewer：HTML 侧 [data-cmd] 元素点击 → postMessage → MessageReceived（负载存于 LastMessagePayloadJson，参数绑定经 eventPayload 源读取）。
                if (context.Control is HtmlViewer htmlViewer)
                {
                    htmlViewer.MessageReceived += (_, _) => context.OnRaised();
                    context.Subscribed = true;
                }
            }),
        };
}
