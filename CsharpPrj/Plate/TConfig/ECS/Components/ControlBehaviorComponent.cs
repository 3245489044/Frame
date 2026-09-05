
using TConfig.ECS;

namespace TConfig.ECS.Components;

/// <summary>
/// 控件行为属性（textbox 等控件的静态行为事实：多行/只读/滚动条）。
/// 布局（LayoutIntent/ProportionalLayout）与值（ControlValue）之外的第二类配置事实，
/// 由 ControlFactory.CreateFromConfig 从 ControlConfig 写入，WinFormsOutputPort.CreateControl 应用物理控件。
/// </summary>
public struct ControlBehaviorComponent : IComponent
{
    public bool Multiline;

    public bool ReadOnly;

    public string ScrollBars;
}
