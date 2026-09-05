
using TConfig;

namespace Modules.Pipeline.Initialization;

public static class PipelineHelper
{

    public static async Task WaitForStagesAsync(
        IStageCoordinator stageCoordinator,
        IReadOnlyList<string> stageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stageCoordinator);
        ArgumentNullException.ThrowIfNull(stageIds);

        if (stageIds.Count == 0)
            return;

        foreach (var stageId in stageIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = stageCoordinator.GetState(stageId);
            if (state == ExecutionStatus.Completed)
                continue;

            var success = await stageCoordinator.WaitForStageAsync(stageId);
            if (!success)
            {
                throw new InvalidOperationException(
                    $"前置阶段 '{stageId}' 等待失败，无法继续执行。");
            }
        }
    }
}
