using System.Collections.Generic;

namespace Share.StateManagement;

/// <summary>
/// 整体状态门面 —— 混合调度机对"整体状态"的最小读写视图（与具体状态宿主解耦）。
/// 仅承载调度所需：当前策略 / 可用策略集 / 运行中执行状态 / 近期转移历史 / 决策遥测 / 归档区。
/// 状态宿主的其余确定性状态（预算/进度/控制位语义）由宿主持有，不在本门面暴露写入口
/// （确定性核心由宿主单一写入入口负责）。
/// 归属：状态宿主契约（Share.StateManagement），由 AI 调度状态机（Share.AiScheduling）经注入消费。
/// </summary>
public interface IStateFacet
{
    /// <summary>当前生效策略 Id。</summary>
    string CurrentStrategyId { get; set; }

    /// <summary>可用策略集（防幻觉目标校验权威来源）。</summary>
    List<string> AvailableStrategyIds { get; }

    /// <summary>运行中执行状态：executorId → 离散状态 Id（字符串承载，契约纯化）。</summary>
    Dictionary<string, string> AgentStatus { get; }

    /// <summary>近期转移历史（只增；压缩由宿主负责）。</summary>
    List<string> RecentTransitions { get; }

    // ---- 决策遥测（只读投影）----
    int Round { get; }
    int BudgetLevel { get; }
    int TokenValue { get; }
    int TokenMax { get; }
    bool Halt { get; }
    int ContinuousFailures { get; }

    /// <summary>读取归档区（转移历史有界窗口弹出时的全量归档，只增不丢）。</summary>
    string? ReadArchive(string key);

    /// <summary>写入归档区。</summary>
    void WriteArchive(string key, string value);
}
