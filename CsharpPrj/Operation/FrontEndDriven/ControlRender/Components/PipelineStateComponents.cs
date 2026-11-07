#nullable enable
using ECS.Core;

namespace FrontEndDriven.ECS.Components;

public struct PendingCapabilityComponent : IComponent
{
}

public struct PendingLayoutComponent : IComponent
{
}

public struct LayoutAppliedComponent : IComponent
{
}

/// <summary>
/// 比例 + 最小像素双约束的分配记录（诊断/可观测）：
/// 记录名义比例宽度、折算后的最小物理宽、实际分配矩形与生效约束（ratio/min/shrunk/overflow）。
/// 写入方：LayoutSystem.ReconcileMinAllocations；消费方：LayoutSystem 比例分支优先采用本组件。
/// </summary>
public struct LayoutAllocationComponent : IComponent
{
    public int Left;
    public int Top;
    public int Width;
    public int Height;
    /// <summary>名义比例宽度（物理 px），用于对比最小像素约束是否生效。</summary>
    public int NominalWidth;
    /// <summary>按 DeviceDpi 折算后的最小物理宽度（物理 px）。</summary>
    public int MinWidthPx;
    /// <summary>生效约束：ratio | min | shrunk | overflow。</summary>
    public string Effective;
    /// <summary>超额无法回收时剩余的溢出像素（0 = 无溢出）。</summary>
    public int OverflowPx;
}

/// <summary>
/// 已投影布局组件（Pillar A·收敛判定下沉 ECS）：记录最近一次物理投影写入的父相对 bounds。
/// 判定"是否需要写"改为对比本标记与 ECS ComputedLayout，不再读物理——
/// 消除「物理读回 → SetBounds → ResumeLayout 二次调整 → 物理≠ECS → 再写」的振荡环。
/// 写入方：LayoutProjector.ProjectScopeOnce；消费方：LayoutProjector 幂等短路（同 ECS 两次投影零写入）。
/// </summary>
public struct ProjectedLayoutComponent : IComponent
{
    public int Left;

    public int Top;

    public int Width;

    public int Height;
}

public struct PendingStyleComponent : IComponent
{
}

public struct StyleAppliedComponent : IComponent
{
}

public enum EventBindingState
{
    Pending,
    Bound,
    Failed
}

public struct EventBindingStateComponent : IComponent
{
    public EventBindingState State;

    public int RetryCount;

    public string ErrorMessage;
}

public struct ParentEntityComponent : IComponent
{
    public Entity ParentEntity { get; set; }
}

/// <summary>
/// 兄弟顺序组件：记录控件在其父容器声明数组中的 0 基下标，把「声明顺序」建模为一等公民。
/// 兄弟序不依赖 ECS 原型/枚举序（该序与声明无关，且会因组件增删、边遍历边移出而打乱）。
/// 写入方：ConfigDrivenControlFactory.ConvertControl（按 config.Controls/Panel1/2Controls 数组下标）；
/// 消费方：Ports.BuildChildRelation → ChildRelation.OrderIndex → WinFormApplier.ApplyRelations
/// 按 (ParentId, OrderIndex) 稳定排序挂载，保证 Tab/分栏/列表等顺序敏感容器与声明一致。
/// </summary>
public struct SiblingOrderComponent : IComponent
{
    public int OrderIndex;
}

/// <summary>
/// TabControl 容器语义组件：标记 tabcontrol 实体并承载选中页索引（选中态显式注入，替代依赖 WinForms 隐式 auto-select）。
/// 消费方：Ports.Collect 读取→ControlDescriptor.SelectedIndex→WinFormApplier.ApplyTabSelection 设置物理 SelectedIndex。
/// TabPage 语义由物理类型（TabPage/TabControl）在挂载端口与可见性检查器裁决，不设独立组件。
/// </summary>
public struct TabContainerComponent : IComponent
{
    public int SelectedIndex;
}