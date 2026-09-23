
using TConfig.ECS;

namespace TConfig.ECS.Components;

public struct EventBindingComponent : IComponent
{
    public string EventName;

    public string FunctionName;

    // D6 收敛：直接使用配置层参数 DTO（TConfig.ParameterBinding），废除 ECS 层同构副本（EcsParameterBinding）
    public TConfig.ParameterBinding[] Parameters;

    public string OutputTarget;

    /// <summary>输出写语义（<see cref="TConfig.OutputWriteModes"/>）；null/空 = replace（覆盖写）。</summary>
    public string? OutputMode;

    /// <summary>该绑定命中函数的执行预算（毫秒）；null = 框架默认（<c>InteractionDefaults.DefaultFunctionTimeoutMs</c>）。</summary>
    public int? FunctionTimeoutMs;
}
