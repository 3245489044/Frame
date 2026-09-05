
using ECS.Core;

namespace FrontEndDriven.ECS.Components;

/// <summary>
/// 宿主布局基准组件（M1·状态真源单向化）：scope 布局基准 = 宿主客户区（0×0 回退父面板客户区）。
/// 由 HostBasisSystem 统一决议并写入 root 实体，替代 RenderStageRunner.AlignLayoutBasisToHost 的渲染前校准补丁。
/// LayoutSystem 计算子控件布局时优先读本组件（意图），ContainerSizeComponent 降为兜底（配置基准）。
/// </summary>
public struct HostBasisComponent : IComponent
{
    /// <summary>布局基准原点 X（宿主全局 form 相对位置；M1·坐标基准修复——使 scope 坐标成为全局 form 相对，对齐 ToParentRelative 契约）。</summary>
    public int Left;

    /// <summary>布局基准原点 Y（宿主全局 form 相对位置）。</summary>
    public int Top;

    public int Width;

    public int Height;

    /// <summary>基准来源："host" / "parentPanel" / "config"（审计用）。</summary>
    public string BasisSource;

    public readonly bool IsValid => Width > 0 && Height > 0;
}
