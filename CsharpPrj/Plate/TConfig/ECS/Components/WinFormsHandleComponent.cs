
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct WinFormsHandleComponent : IComponent
{
    public IntPtr Handle;

    public string ControlType;
}
