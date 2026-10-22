namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界触发通道（脊神经反应式 / 皮层主动式，并行采集、仲裁合并）。
/// Reactive = 脊神经反射弧：确定性、无 LLM、低延迟、短路优先；
/// Proactive = 皮层主动：LLM/执行智能体自评提议，需门控。
/// </summary>
public enum TriggerChannel
{
    Reactive,
    Proactive,
}

/// <summary>边界触发种类（区分仲裁短路口与来源，供观测与裁决）。</summary>
public enum BoundaryTriggerKind
{
    None,

    /// <summary>脊神经：协议级硬断（stopReason=model_context_window_exceeded）。</summary>
    ProtocolStop,

    /// <summary>脊神经：token 耗尽且无进展。</summary>
    TokenExhaust,

    /// <summary>脊神经：错误熔断（错误分类）。</summary>
    ErrorClass,

    /// <summary>脊神经：上下文压力（水位/硬上限）。</summary>
    ContextPressure,

    /// <summary>脊神经：信息增益趋零。</summary>
    InfoGain,

    /// <summary>脊神经：复杂度超界。</summary>
    Complexity,

    /// <summary>脊神经：空间越界/禁用路径。</summary>
    Spatial,

    /// <summary>皮层：LLM/执行智能体主动自评。</summary>
    LlmSelfAssessment,
}
