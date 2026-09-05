
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct EventBindingComponent : IComponent
{
    public string EventName;

    public string FunctionName;

    // D6 收敛：直接使用配置层参数 DTO（TConfig.ParameterBinding），废除 ECS 层同构副本（EcsParameterBinding）
    public TConfig.ParameterBinding[] Parameters;

    public string OutputTarget;
}
