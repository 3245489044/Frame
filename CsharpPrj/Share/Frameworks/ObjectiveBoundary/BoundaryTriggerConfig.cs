using System.Collections.Generic;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 双通道触发配置（L3，运行时可变；boundary-config.json triggers 块）。
/// Reactive = 脊神经反应式；Proactive = 皮层主动式。两者并行采集，仲裁器合并。
/// </summary>
public sealed class BoundaryTriggerConfig
{
    public ReactiveTriggerConfig Reactive { get; set; } = new();

    public ProactiveTriggerConfig Proactive { get; set; } = new();
}

/// <summary>反应式（脊神经）通道配置。硬信号短路永远可用；enabled=false 时边界框架退化为健康（由仲裁器处理）。</summary>
public sealed class ReactiveTriggerConfig
{
    public bool Enabled { get; set; } = true;
}

/// <summary>主动式（皮层）通道配置：门控阈值 + 动作白名单 + advisory 降级。</summary>
public sealed class ProactiveTriggerConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>主动式意图的最低置信度（低于即忽略）。</summary>
    public double MinConfidence { get; set; } = 0.4;

    /// <summary>主动式允许请求的边界动作白名单（escalate 默认不在内，防过度升级，对齐 ANTI-EXCESSIVE-SWITCH）。</summary>
    public List<string> ActionWhitelist { get; set; } = new()
    {
        BoundaryActionKind.Compact,
        BoundaryActionKind.ResubmitWithInfo,
        BoundaryActionKind.Rescope,
    };

    /// <summary>是否将主动式 compact 在上下文压力低时降级为 advisory（防 LLM 过度压缩）。</summary>
    public bool AdvisoryWhenContextLow { get; set; } = true;

    /// <summary>advisory 降级判定的上下文压力阈值（低于即视为"压力低"→ 主动式 compact 降级为提示不触发）。</summary>
    public double AdvisoryContextThreshold { get; set; } = 0.4;
}
