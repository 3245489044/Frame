using System.Threading;
using System.Threading.Tasks;

namespace Share.AiScheduling;

/// <summary>
/// 状态导演（提议端口，自由探索源）。
/// 读导演上下文 → 返回整体状态转移提议（<see cref="StateTransitionProposal"/>）；null = 无提议（stay，fail-safe）。
/// 仅提议、不直接变更状态 —— 校验（<see cref="ITransitionGuard"/>）与应用在计算层完成。
/// </summary>
public interface IStateDirector
{
    Task<StateTransitionProposal?> DecideAsync(
        SchedulerDirectorContext context,
        CancellationToken cancellationToken = default);
}
