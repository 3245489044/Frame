using System.Threading;
using System.Threading.Tasks;

namespace Share.AiScheduling;

/// <summary>
/// AI 调度状态机门面：AdvanceAsync = 提议（自由探索）+ 计算守卫校验（策略完全实施）→ 返回决策。
/// 非限定：本门面无任何 max-iterations / halt / IsCompleted 终止逻辑；继续/终止由外部裁决。
/// 只裁决"整体状态怎么变"，继续/终止由宿主（watchdog / 取消令牌）裁决。
/// </summary>
public interface ISchedulerStateMachine
{
    Task<SchedulerDecision> AdvanceAsync(
        SchedulerDirectorContext context,
        CancellationToken cancellationToken = default);
}
