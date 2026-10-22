using System.Threading;
using System.Threading.Tasks;

namespace Share.AiScheduling;

/// <summary>
/// 导演裁决（提议域 / 失败域分离）：<c>Proposal</c> = 可施加提议；<c>Stop</c> = 显式失败事实
/// （禁以 null 表达失败——null 无法区分「本轮无提议」与「导演坏了」）。
/// </summary>
public abstract record DirectorDecision
{
    private DirectorDecision() { }

    public sealed record Proposal(StateTransitionProposal Value) : DirectorDecision;

    public sealed record Stop(string Code, string Message) : DirectorDecision;
}

/// <summary>导演停止码注册表（禁自由字符串）。</summary>
public static class DirectorStops
{
    public const string NoProposal = "DIRECTOR_NO_PROPOSAL";
    public const string LlmUnavailable = "DIRECTOR_LLM_UNAVAILABLE";
    public const string ParseFailed = "DIRECTOR_PARSE_FAILED";
    public const string ContractDrift = "DIRECTOR_CONTRACT_DRIFT";
    public const string NoIntentSource = "DIRECTOR_NO_INTENT_SOURCE";
    public const string InputMissing = "DIRECTOR_INPUT_MISSING";
}

/// <summary>
/// 状态导演（提议端口，自由探索源）。
/// 读导演上下文 → 返回整体状态转移提议（<see cref="StateTransitionProposal"/>）。
/// 仅提议、不直接变更状态 —— 校验（<see cref="ITransitionGuard"/>）与应用在计算层完成。
/// <para>
/// 契约升级：消费方应调用 <see cref="DecideOutcomeAsync"/>（显式 <see cref="DirectorDecision"/>）；
/// <see cref="DecideAsync"/> 仅为兼容投影（null 语义已不可区分，不得用于新代码）。
/// </para>
/// </summary>
public interface IStateDirector
{
    Task<StateTransitionProposal?> DecideAsync(
        SchedulerDirectorContext context,
        CancellationToken cancellationToken = default);

    /// <summary>显式裁决（默认适配：null → <see cref="DirectorStops.NoProposal"/>，失败不静默）。</summary>
    async Task<DirectorDecision> DecideOutcomeAsync(
        SchedulerDirectorContext context,
        CancellationToken cancellationToken = default)
    {
        var proposal = await DecideAsync(context, cancellationToken).ConfigureAwait(false);
        return proposal is null
            ? new DirectorDecision.Stop(DirectorStops.NoProposal, "导演未产出可施加提议。")
            : new DirectorDecision.Proposal(proposal);
    }
}
