using System;
using System.Collections.Generic;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界框架完全参数化配置（L3，运行时可变）。由 Agent.Core.BoundaryConfigLoader 从唯一配置根
/// <c>agent-config.json</c> 的 <c>AgentConfig:Boundary</c> 节加载（单路：缺失即抛，无内置默认）。
/// 宿主可在组合根（AgentConfig.Boundary）按 Profile 覆写。三块：Logical / Spatial / Policy。
/// </summary>
public sealed class BoundaryConfig
{
    /// <summary>上下文水位代码默认单源（对应 axioms %CTX_WATERMARK；声明面为唯一权威，代码默认引用本常量）。</summary>
    public const double DefaultContextWatermark = 0.85;

    /// <summary>上下文预算上限代码默认单源（字符数）。</summary>
    public const int DefaultContextLimitChars = 400_000;

    public string Version { get; set; } = "1.0.0";

    public LogicalBoundaryConfig Logical { get; set; } = new();

    public SpatialBoundaryConfig Spatial { get; set; } = new();

    public BoundaryPolicyConfig Policy { get; set; } = new();

    /// <summary>双通道触发配置（脊神经反应式 + 皮层主动式，并行采集、仲裁合并）。</summary>
    public BoundaryTriggerConfig Triggers { get; set; } = new();
}

/// <summary>逻辑边界（自省）参数：上下文极限 / 信息增益速率 / 复杂度 / 权重与触发阈值。</summary>
public sealed class LogicalBoundaryConfig
{
    /// <summary>上下文预算上限（字符数，P4 真实 token 可用时按 token 折算）—— 默认单源 = <see cref="BoundaryConfig.DefaultContextLimitChars"/>。</summary>
    public int ContextLimitChars { get; set; } = BoundaryConfig.DefaultContextLimitChars;

    /// <summary>上下文水位（对应 axioms CTX_WATERMARK）：超过即压力趋 1。默认单源 = <see cref="BoundaryConfig.DefaultContextWatermark"/>。</summary>
    public double ContextWatermark { get; set; } = BoundaryConfig.DefaultContextWatermark;

    /// <summary>信息增益速率下限（Δ输出长度/Δ轮次 的归一阈值），低于即判"增益趋零"。</summary>
    public double InfoGainMinRate { get; set; } = 0.05;

    /// <summary>信息增益统计窗口（轮次）。</summary>
    public int InfoGainWindow { get; set; } = 3;

    /// <summary>信息增益速率 EWMA 平滑系数 α（0~1，越大越跟随单轮；越小越平滑抗抖）。
    /// rate_t = WeightedAverage(窗口 rateᵢ, α·(1-α)^i)。默认 0.5。</summary>
    public double InfoGainAlpha { get; set; } = 0.5;

    /// <summary>逻辑边界轮次上限（对齐 MAX_PROGRESS 语义的观测基线）。</summary>
    public int MaxRounds { get; set; } = 3;

    /// <summary>复杂度上限（累计无进展/步骤数），超限判复杂度压力。</summary>
    public int ComplexityMaxSteps { get; set; } = 25;

    /// <summary>边界压力权重（三压力加权 → Φ 的工程近似）。</summary>
    public BoundaryWeights SignalWeights { get; set; } = new();

    /// <summary>触发边界重构的压力阈值（Score ≥ 该值即 Triggered，作为迟滞触发档 engage）。</summary>
    public double ReconstructThreshold { get; set; } = 0.8;

    /// <summary>上下文压力维度的独立阈值（0~1；≤0 时回退 <see cref="ReconstructThreshold"/>）。
    /// 三量纲不得共用一个阈值（ObjectiveBoundary.md §4.1）：上下文/复杂度是压力（越大越坏），信息增益是速率（越小越坏）。</summary>
    public double ContextThreshold { get; set; } = 0d;

    /// <summary>复杂度压力维度的独立阈值（0~1；≤0 时回退 <see cref="ReconstructThreshold"/>）。</summary>
    public double ComplexityThreshold { get; set; } = 0d;

    /// <summary>迟滞复位阈值（release）：刚重构后 Score 需回落到该值以下才解除迟滞，
    /// 允许再次触发；否则抑制重构后立刻再次触发（防打转，每次重构有 token 成本）。默认 0.6。</summary>
    public double HysteresisReleaseThreshold { get; set; } = 0.6;

    /// <summary>迟滞保持窗口（轮次）：重构后 N 轮内若 Score 仍高于复位阈值则抑制再次触发；
    /// 超过窗口自然解除（避免 ReconstructCount 只增导致永久禁用 score 触发）。默认 1。</summary>
    public int HysteresisWindowRounds { get; set; } = 1;

    /// <summary>逻辑边界允许的重构动作（顺序即优先级）。</summary>
    public List<string> Actions { get; set; } = new()
    {
        BoundaryActionKind.Compact,
        BoundaryActionKind.ResubmitWithInfo,
        BoundaryActionKind.Rescope,
        BoundaryActionKind.Escalate,
    };
}

/// <summary>三压力权重（求和=1）。</summary>
public sealed class BoundaryWeights
{
    public double Context { get; set; } = 0.5;

    public double InfoGain { get; set; } = 0.3;

    public double Complexity { get; set; } = 0.2;

    /// <summary>累计无进展步数权重（复杂度压力的深度项 w₂）。默认 0（不启用，保持行为不变）；
    /// 启用时与 Complexity 共同构成 complexity = w·(Round/maxRounds) + ComplexitySteps·(NoProgress/maxSteps)。</summary>
    public double ComplexitySteps { get; set; } = 0;
}

/// <summary>空间边界参数：Cline 工作根目录与范围守卫。</summary>
public sealed class SpatialBoundaryConfig
{
    /// <summary>基准根目录（默认仓库根）；未设置时评估器回退当前工作目录。</summary>
    public string? BaseRoot { get; set; }

    /// <summary>允许访问的根目录白名单。</summary>
    public List<string> AllowedRoots { get; set; } = new();

    /// <summary>禁止访问的路径子串（如 .git / node_modules）。</summary>
    public List<string> DenyPaths { get; set; } = new()
    {
        "\\.git",
        "\\node_modules",
    };

    /// <summary>单轮最大文件读取量（超出判资源压力，可选）。</summary>
    public int MaxFileReadsPerRound { get; set; } = 20;

    /// <summary>允许的最大目录深度。</summary>
    public int MaxDepth { get; set; } = 4;

    /// <summary>是否启用 Cline 数据目录隔离（ClineRunOptions.DataDir）。</summary>
    public bool DataDirIsolation { get; set; }

    /// <summary>隔离用默认数据目录（DataDirIsolation=true 时的落点；为空则取系统临时目录）。</summary>
    public string? DefaultDataDir { get; set; }

    /// <summary>越界时的策略：deny=拒绝并在提示词告知；expand=若在允许范围内则扩展有效根。</summary>
    public string ExpandPolicy { get; set; } = "deny";

    /// <summary>派生副本并覆写 BaseRoot（D6：收敛 DefaultBoundaryFramework 的多宿主 baseRoot 注入样板，避免共享单例被改写）。</summary>
    public SpatialBoundaryConfig WithBaseRoot(string baseRoot) => new()
    {
        BaseRoot = baseRoot,
        AllowedRoots = AllowedRoots,
        DenyPaths = DenyPaths,
        MaxFileReadsPerRound = MaxFileReadsPerRound,
        MaxDepth = MaxDepth,
        DataDirIsolation = DataDirIsolation,
        DefaultDataDir = DefaultDataDir,
        ExpandPolicy = ExpandPolicy,
    };
}

/// <summary>边界策略：边界被破坏时的循环行为。</summary>
public sealed class BoundaryPolicyConfig
{
    /// <summary>逻辑边界被破坏时的行为：reconstruct_and_continue（重构后继续一级搜索）。</summary>
    public string OnLogicalBreach { get; set; } = "reconstruct_and_continue";

    /// <summary>空间越界时的行为：deny_and_hint（拒绝并在提示词告知边界）。</summary>
    public string OnSpatialViolation { get; set; } = "deny_and_hint";

    /// <summary>无法执行建议重构动作时的兜底动作。</summary>
    public string Fallback { get; set; } = BoundaryActionKind.Compact;
}

/// <summary>边界重构动作关键字（完全参数化的动作词汇表权威）。</summary>
public static class BoundaryActionKind
{
    /// <summary>压缩历史（降低上下文压力）。</summary>
    public const string Compact = "compact";

    /// <summary>回写"需要改变输入条件"（等价文档 4.2 的"加日志"边界重构）。</summary>
    public const string ResubmitWithInfo = "resubmit_with_info";

    /// <summary>收窄问题域（重规划聚焦子问题）。</summary>
    public const string Rescope = "rescope";

    /// <summary>升级边界（提高预算/更换模型等）。</summary>
    public const string Escalate = "escalate";

    /// <summary>空间边界：扩界（在允许范围内扩展有效根）。</summary>
    public const string ExpandRoot = "expand_root";

    /// <summary>空间边界：切换工作根目录。</summary>
    public const string SwitchCwd = "switch_cwd";

    /// <summary>空间边界：启用数据目录隔离。</summary>
    public const string IsolateDataDir = "isolate_data_dir";

    /// <summary>无动作（边界健康）。</summary>
    public const string None = "none";
}
