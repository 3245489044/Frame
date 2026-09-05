using Share.TaskStatusPool.Routing;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 从通用执行标识符（<see cref="ExecutionFlag"/>）派生"是否需 LLM"的自主标记适配器：
/// <see cref="ExecutionFlag.Llm"/> → 需 LLM；<see cref="ExecutionFlag.Rule"/> / <see cref="ExecutionFlag.Deterministic"/> → 非自主（零 LLM）。
/// 与既有 <see cref="Agent.Operation.Routing.SchedulingModeAutonomy"/> 并存，二者都喂给同一个 <see cref="TaskRouter"/>。
/// 泛化自 SchedulingModeAutonomy：标识符是三层共用的统一概念（设计 §3.2），本适配器只编排、不写边（SingleImplementation / TH-12）。
/// </summary>
public sealed class ExecutionFlagAutonomy : ITaskAutonomy
{
    private readonly ExecutionFlag _flag;

    public ExecutionFlagAutonomy(ExecutionFlag flag) => _flag = flag;

    /// <summary>仅 Llm 声明需 LLM；Rule / Deterministic 非自主（零 LLM）。</summary>
    public bool RequiresLlm => _flag == ExecutionFlag.Llm;

    public static ITaskAutonomy From(ExecutionFlag flag) => new ExecutionFlagAutonomy(flag);
}
