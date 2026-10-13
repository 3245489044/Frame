#nullable enable

using System;

namespace UiBackend.Abstractions;

/// <summary>
/// 桥模块清单（L1 通用契约）——承载于活 document 的前端行为模块位（点击/补丁态/键控对齐/刷新）。
/// 「就绪」不再等于单一 <c>__domBridgeInstalled</c> 标志，而是本清单全位就绪，消除「点击桥就绪但补丁/对齐模块缺失 → 区域写永久降级」。
/// </summary>
[Flags]
public enum UiBridgeModules
{
    /// <summary>无模块就绪。</summary>
    None = 0,
    /// <summary>点击桥（<c>[data-cmd]</c>/<c>[data-event]</c> → postMessage）。</summary>
    Click = 1,
    /// <summary>补丁态保存/恢复模块。</summary>
    Patch = 2,
    /// <summary>键控对齐引擎（reconcile）。</summary>
    Reconcile = 4,
    /// <summary>反向刷新桥（宿主推送 + 兜底轮询）。</summary>
    Refresh = 8,
    /// <summary>全部模块（就绪判据）。</summary>
    All = Click | Patch | Reconcile | Refresh,
}

/// <summary>桥模块就绪状态（模块位 + 版本；版本失配 → 视为不完整，供热重载/ALC 代数校验）。</summary>
public readonly record struct UiBridgeModuleState(UiBridgeModules Modules, int Version = 1)
{
    /// <summary>未就绪（空状态）。</summary>
    public static UiBridgeModuleState None => new(UiBridgeModules.None);

    /// <summary>是否已就绪全部模块。</summary>
    public bool IsComplete => (Modules & UiBridgeModules.All) == UiBridgeModules.All;

    /// <summary>是否含指定模块位。</summary>
    public bool Has(UiBridgeModules module) => (Modules & module) == module;

    /// <summary>缺失的模块位（诊断用）。</summary>
    public UiBridgeModules Missing => UiBridgeModules.All & ~Modules;
}
