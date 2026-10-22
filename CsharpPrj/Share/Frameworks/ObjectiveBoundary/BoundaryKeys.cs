namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界框架键名唯一权威。跨轮状态（AxiomContext.Data / 骨架数据空间 vars）中
/// 承载空间边界（Cwd/DataDir）与逻辑边界状态（BoundaryState/ReconstructHint）的键。
/// 消除散落魔法字符串，集中可观测。
/// </summary>
public static class BoundaryKeys
{
    /// <summary>Cline 有效工作根目录（空间边界落地：消费方 ClineAgentExecutor → ClineRunOptions.Cwd）。</summary>
    public const string Cwd = "boundary_cwd";

    /// <summary>Cline 数据目录隔离（可选：ClineRunOptions.DataDir）。</summary>
    public const string DataDir = "boundary_data_dir";

    /// <summary>已解析的空间边界根目录（评估产物，供提示词/日志）。</summary>
    public const string RootDirectory = "boundary_root";

    /// <summary>逻辑边界运行期状态快照（BoundaryContext，跨轮持久化）。</summary>
    public const string BoundaryState = "boundary_state";

    /// <summary>边界重构后的下一步提示（供 provide_prompt 消费，等价"需要改变输入条件"）。</summary>
    public const string ReconstructHint = "boundary_reconstruct_hint";

    /// <summary>上一轮边界评估（PostRound 产物，供观测）。</summary>
    public const string LastAssessment = "boundary_last_assessment";
}
