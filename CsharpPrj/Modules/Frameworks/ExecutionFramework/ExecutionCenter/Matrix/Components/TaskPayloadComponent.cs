
using TConfig.ECS;

namespace ExecutionCenter.Matrix.Components;

public struct TaskPayloadComponent : IComponent
{
    public string TaskType;

    public string PayloadJson;

    public DateTime CreatedAt;

    public TaskPayloadComponent(string taskType, string payloadJson)
    {
        TaskType = taskType ?? throw new ArgumentNullException(nameof(taskType));
        PayloadJson = payloadJson ?? throw new ArgumentNullException(nameof(payloadJson));
        CreatedAt = DateTime.UtcNow;
    }
}