#nullable enable
using ECS.Core;

namespace FrontEndDriven.ECS.Components;

/// <summary>
/// 可见性语义意图：把「物理不可见」分类为语义枚举，可见性契约校验器据此决定断言强度与自愈边界，
/// 替代逐类硬编码豁免（此前 tab 非选中页 / 空容器需在检查器内 if 补丁）。
/// </summary>
public enum VisibilityIntent
{
    /// <summary>应可见（默认）：物理 Visible 必须为 true，且尺寸>0、inHost。违规视为真实缺陷并触发自愈。</summary>
    ExpectVisible,

    /// <summary>语义隐藏：物理不可见是正确状态（如 Tab 非选中页、条件区）。仅断言尺寸/位置，不触发自愈。</summary>
    SemanticHidden,

    /// <summary>生命周期态：空载/按需加载的合法中间态（如空容器 0x0）。仅断言未溢出，不触发自愈。</summary>
    DeferredHidden
}

/// <summary>
/// 控件可见性语义声明（配置可覆盖的扩展点）。
/// 权威优先级：Tab 运行时结构语义 &gt; 生命周期态(空容器) &gt; 本组件(配置声明) &gt; 默认 ExpectVisible。
/// 缺省（无组件）= ExpectVisible；本组件由 config hiddenSemantics 填充（Phase ②）。
/// </summary>
public struct VisibilitySemanticsComponent : IComponent
{
    public VisibilityIntent Intent;

    /// <summary>语义原因（诊断用，如 "tab-page-child" / "empty-container" / "config-declared"）。</summary>
    public string Reason;

    /// <summary>子控件是否继承该语义（tab 页：true；空容器：false）。</summary>
    public bool InheritToChildren;
}
