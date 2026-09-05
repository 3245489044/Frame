using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 通用执行标识符（ExecutionFlag）—— history / task / node 三层实体共用的"运行方式声明"受控词汇。
/// 泛化自既有 <see cref="SchedulingMode"/>（原仅 task 级）：不再只属于 task，而是三层共用的统一概念。
/// 标识符是**反应式**的：被设置/切换后，经 <see cref="FlagConductor"/> 自动触发「解析 → 路由 → 运行 → 回切 Std」闭环。
/// 权威设计：《Projects/Agent/doc/执行标识符框架设计.md》。
/// </summary>
public enum ExecutionFlag
{
    /// <summary>未显式声明 → 沿引用边继承父层（history→task→node 逐层收窄）；都 Auto → 回退 Deterministic（零 LLM）。</summary>
    Auto,

    /// <summary>LLM 反应式调用（自主生成 / 工具）—— 对应原 Hybrid。</summary>
    Llm,

    /// <summary>规则表驱动（ur_config）—— 对应原 Config，零 LLM。</summary>
    Rule,

    /// <summary>纯规则迭代（零 LLM）—— 对应原 Deterministic。</summary>
    Deterministic,

    /// <summary>标准状态 / 回切目标（运行完成后的默认落点；等价于"未显式声明 → 继承父层"）。</summary>
    Std,
}

/// <summary>
/// 执行标识符在 LiteDB 引用图资源元数据（<c>Resource.Metadata</c>）上的存取键（唯一权威，SingleImplementation / TH-12）。
/// </summary>
public static class ExecutionFlagMetadata
{
    /// <summary>声明/生效的执行标识符（值如 "Auto"/"Llm"/"Rule"/"Deterministic"/"Std"）。</summary>
    public const string FlagKey = "exec.flag";

    /// <summary>该实体生命周期（值如 "idle"/"flagged"/"routed"/"running"/"completed"/"faulted"）。</summary>
    public const string LifecycleKey = "exec.lifecycle";

    /// <summary>运行结果摘要 / 引用（可选）。</summary>
    public const string ResultKey = "exec.result";

    /// <summary>Auto 时解析到的父层（归因/观测；值如 "history:demo" / "task:x"）。</summary>
    public const string ParentKey = "exec.parent";
}

/// <summary>
/// 执行标识符 ↔ 既有调度模式映射（设计 §3.2）：标识符系统经本映射把 <see cref="ExecutionFlag.Llm"/>
/// 落到 task 的 <see cref="SchedulingMode.Hybrid"/>（或直接喂 <see cref="ExecutionFlagAutonomy"/>），
/// 执行仍走既有 <see cref="Share.TaskStatusPool.Routing.TaskRouter"/> / 任务池路径，不新增执行引擎。
/// </summary>
public static class ExecutionFlagMapping
{
    /// <summary>执行标识符 → 既有 <see cref="SchedulingMode"/>；<see cref="ExecutionFlag.Auto"/> / <see cref="ExecutionFlag.Std"/> 无直接对应（返回 null，交由解析沿边继承）。</summary>
    public static SchedulingMode? ToSchedulingMode(this ExecutionFlag flag)
        => flag switch
        {
            ExecutionFlag.Llm => SchedulingMode.Hybrid,
            ExecutionFlag.Rule => SchedulingMode.Config,
            ExecutionFlag.Deterministic => SchedulingMode.Deterministic,
            _ => null,
        };

    /// <summary>是否"未显式声明需继承父层"（Auto / Std 均视为需沿边解析生效标志）。</summary>
    public static bool IsInherit(this ExecutionFlag flag)
        => flag is ExecutionFlag.Auto or ExecutionFlag.Std;
}
