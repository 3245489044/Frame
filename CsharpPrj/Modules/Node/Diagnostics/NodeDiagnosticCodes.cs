namespace Modules.Node.Diagnostics;

/// <summary>节点诊断码单一词表（生产与测试共同引用，禁字面量各写一份）。</summary>
public static class NodeDiagnosticCodes
{
    /// <summary>线程约定解析缺位置声明（键文法单源 = 域声明；对象域 = 行键）。</summary>
    public const string PlacementUnbound = "row-key.placement-unbound";

    /// <summary>溯源边端点不可解析（边跳过，不静默）。</summary>
    public const string EdgeEndpointUnresolved = "edge.endpoint-unresolved";

    /// <summary>声明了 token 预算但未装配估算器（只执行条数上限）。</summary>
    public const string BudgetEstimatorMissing = "budget.estimator-missing";

    /// <summary>写计划为空（整体拒绝）。</summary>
    public const string WritePlanEmpty = "write.plan-empty";

    /// <summary>写计划非法（Id 重复 / 依赖环 / 缺依赖 / 短路悬空）。</summary>
    public const string WritePlanInvalid = "write.plan-invalid";

    /// <summary>写步骤介质端口未装配。</summary>
    public const string WritePlanHandlerMissing = "write.plan-handler-missing";

    /// <summary>写步骤执行失败（计划整体拒绝 + 逆序补偿）。</summary>
    public const string WriteStepFailed = "write.step-failed";

    /// <summary>补偿失败（不掩盖原始失败，仅留痕）。</summary>
    public const string WriteCompensateFailed = "write.compensate-failed";

    /// <summary>LLM-free 断言失败。</summary>
    public const string WriteLlmFreeViolated = "write.llm-free-violated";

    /// <summary>本轮无可归因终答（不伪造终答）。</summary>
    public const string ArtifactTerminalMissing = "artifact.terminal-missing";

    /// <summary>容器 Kind 未声明（按空容器处理）。</summary>
    public const string ContainerUnregistered = "container.unregistered";

    /// <summary>执行声明继承出现环（停止解析）。</summary>
    public const string ExecutionInheritCycle = "execution.inherit-cycle";

    /// <summary>继承边的父端点不可解析。</summary>
    public const string ExecutionInheritEndpointInvalid = "execution.inherit-endpoint-invalid";

    /// <summary>同一地址存在多条执行声明（保留首个）。</summary>
    public const string ExecutionBindingDuplicate = "execution.binding-duplicate";

    /// <summary>标签 token 未识别（拒绝归一，不静默丢）。</summary>
    public const string TagsUnrecognized = "tags.unrecognized";
}
