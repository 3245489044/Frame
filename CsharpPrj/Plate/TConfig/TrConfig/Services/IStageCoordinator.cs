
namespace TConfig;

public interface IStageCoordinator
{
    void RegisterStage(string stageId, string description, int timeoutMs = 30000);

    void Start();

    Task<bool> WaitForStageAsync(string stageId);

    void CompleteStage(string stageId, bool success = true, string? errorMessage = null);

    ExecutionStatus GetState(string stageId);
}
