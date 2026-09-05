
#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using ECS.Core;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Shared;
using TConfig.Controls;

namespace FrontEndDriven.ControlInteraction.EventBinding;

/// <summary>
/// RenderPort 输入采集端口（M3·事件与命令收编 + P2·值语义契约订阅 + 共存方案 R2·收缩）：
/// 只做"物理值变更信号 → 回灌 ECS 状态"（ListView 选中 / TextBox 输入 / CheckBox 勾选等，经值语义契约声明）。
/// 不再采集 Click/DoubleClick 触发函数——"事件→函数"由物理绑定链（EventBindingSystem 真实挂接）单通道负责，
/// 消除双触发（物理 handler 与采集端口同时 Enqueue）。
/// 防循环：程序化 set（ECS→物理投影）后物理值 == ECS 值 → 跳过，仅用户操作回灌。
/// </summary>
public sealed class InputCapturePort
{
    private readonly IEcsStateAccessor _stateAccessor;
    private readonly IControlService _valueService;

    private static readonly object s_subscribedSentinel = new();

    // E4·去泄漏：以弱引用持有控件 key——控件销毁且无强引用后条目随 GC 自动回收，
    // 避免"反复 load-config-show / 销毁 scope"场景下字典以控件为 key 永久持有强引用、永续增长。
    private readonly ConditionalWeakTable<Control, object> _subscribed = new();

    public InputCapturePort(IEcsStateAccessor stateAccessor, IControlService valueService)
    {
        _stateAccessor = stateAccessor ?? throw new ArgumentNullException(nameof(stateAccessor));
        _valueService = valueService ?? throw new ArgumentNullException(nameof(valueService));
    }

    // 诊断计数：弱引用表 Count 为方法；条目随 GC 回收，反映当前仍存活且已订阅的控件数。
    public int SubscribedCount => _subscribed.Count();

    /// <summary>挂接物理控件"值变更信号"采集（幂等：同一控件实例只挂一次）。</summary>
    public void Subscribe(Control control, Entity entity)
    {
        if (control == null || !entity.IsValid) return;
        if (_subscribed.TryGetValue(control, out _)) return;

        var controlName = control.Name;
        if (string.IsNullOrWhiteSpace(controlName))
            return;

        _subscribed.Add(control, s_subscribedSentinel);

        // P2·契约驱动的值变更信号订阅：ValueContractRegistry 声明"语义→事件名"，不再按控件类型散落 if 分支
        var contract = _valueService.GetContract(control.GetType());
        if (contract != null)
        {
            foreach (var (semantic, eventName) in contract.ValueChangedEvents)
            {
                SubscribeValueChanged(control, controlName, semantic, eventName);
            }
        }

        FrontEndDrivenLog.Log("InputCapturePort_Subscribed",
            new { controlName, entityId = entity.Id.ToString(), controlType = control.GetType().Name });
    }

    /// <summary>值变更信号 → 回灌 ECS（物理 ≠ ECS 时）。</summary>
    private void OnValueChanged(Control control, string controlName, ValueSemantic semantic)
    {
        if (control.IsDisposed) return;

        var physical = _valueService.GetValue(control, semantic)?.ToString();
        if (physical == null) return;

        // 防循环：程序化 set（ECS→物理投影）后物理值 == ECS 值 → 跳过；仅用户操作（物理 ≠ ECS）回灌 ECS 状态
        var current = _stateAccessor.GetValue(controlName);
        if (string.Equals(current, physical, StringComparison.Ordinal))
            return;

        _stateAccessor.SetValue(controlName, physical);
    }

    /// <summary>契约事件名 → 强类型订阅（单点收口：值变更信号的物理订阅适配；事件名为契约声明的有限集）。</summary>
    private void SubscribeValueChanged(Control control, string controlName, ValueSemantic semantic, string eventName)
    {
        void OnChanged() => OnValueChanged(control, controlName, semantic);

        // 统一事件订阅分派（EventSubscriptionHelper）：契约事件名（PascalCase）大小写不敏感匹配，
        // checkedchanged/selectedindexchanged/valuechanged 按控件实际类型分派——
        // 消除旧强转 CheckBox 对 RadioButton 抛 InvalidCastException 的缺陷（E1）。
        // 控件类型不匹配时（契约声明但物理控件不支持）仅记录诊断日志，保持静默跳过语义。
        if (!EventSubscriptionHelper.TrySubscribe(control, eventName, OnChanged))
        {
            FrontEndDrivenLog.Log("InputCapturePort_SubscribeValueChanged_unsupported",
                new { controlName, eventName, controlType = control.GetType().Name });
        }
    }
}

