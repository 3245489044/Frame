using Operation.StateBlock.Layering;

namespace Operation.StateBlock.Decision;

/// <summary>
/// 决策框架在 StateCenter 的 canonical 键 —— 确定性核心（Budget/Token/Flag/Round/LastEval）与轮决策记录
/// 在 Session 层（scope=任务）的分区键名唯一权威，替代魔法键（如旧 <c>Outputs["decision"]</c>，D4 根治）。
/// 域命名空间经 <see cref="StateLayerScheme.ForDomain"/> 三段展开（StateBlock.Decision.Config/.Session/.Transient），
/// scope 由调用方给定（宿主侧用任务/sessionId）；本类仅承载键名与 scheme，不含 Agent 依赖。
/// 对应文档 2026-09-04 StateHoldDriven §2.2 LoopKeys / §3.2。
/// </summary>
public static class LoopKeys
{
    /// <summary>决策域三段命名空间域名（StateLayerScheme.ForDomain 展开）。</summary>
    public const string Domain = "StateBlock.Decision";

    // ── 确定性核心（Session 层, scope=任务）──（R8：仅 LoopStateStore.Save / StateDrivenRoundLifecycle 写）

    public const string BudgetLevel = "core.budget.level";   // int, level∈{0..3}
    public const string TokenValue = "core.token.value";     // int
    public const string TokenMax = "core.token.max";         // int（仅框架可调，边界经受管提议）
    public const string FlagHalt = "core.flag.halt";         // bool
    public const string Round = "core.round";                // int（从 1 起）
    public const string UserInput = "core.user_input";       // string?
    public const string LastResultText = "core.last_result"; // string?
    public const string LastEval = "core.last_eval";         // EvalOutcome.ToString()

    // ── 轮决策记录（Session 层, scope=任务；审计/回放）──

    public const string DecisionLatest = "decision.latest";  // DecisionRecord 序列化（JSON 字符串）

    /// <summary>决策域分层 scheme（Config/Session/Transient 三段命名空间）。</summary>
    public static StateLayerScheme BuildScheme() => StateLayerScheme.ForDomain(Domain);
}
