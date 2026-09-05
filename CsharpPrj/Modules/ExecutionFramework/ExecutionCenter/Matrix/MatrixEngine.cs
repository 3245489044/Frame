
using BasicMethod.SortMent;
using ECS.Core;
using ExecutionCenter.Abstractions;
using ExecutionCenter.Matrix.Components;
using ExecutionCenter.Matrix.Systems;

namespace ExecutionCenter.Matrix;

public class MatrixEngine : ITaskExecutorPool
{
    private readonly EcsWorld _world;
    private readonly RouterSystem _router;
    private bool _disposed;

    public EntityManager EntityManager => _world.EntityManager;

    public RouterSystem Router => _router;

    public RetrySystem Retry { get; }

    public MonitorSystem Monitor { get; }

    public MatrixEngine(EcsWorld externalWorld)
    {
        _world = externalWorld ?? throw new ArgumentNullException(nameof(externalWorld));

        _world.SystemManager.RegisterSystem(new SchedulerSystem());
        _router = _world.SystemManager.RegisterSystem(new RouterSystem());
        Retry = _world.SystemManager.RegisterSystem(new RetrySystem());
        Monitor = _world.SystemManager.RegisterSystem(new MonitorSystem());
    }

    public Task<ITaskResponse> SubmitAsync(ITaskMessage task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        TaskArchetype.CreateRoutedTask(
            EntityManager,
            task.TaskType,
            SerializePayload(task),
            task.TaskType,
            targetEnvironment: "CLI",
            priority: task.Priority);

        return Task.FromResult<ITaskResponse>(new Messages.TaskResponse(task.MessageId, true));
    }

    public void RegisterHandler(string taskType,
        Func<ITaskMessage, object?, CancellationToken, Task<ITaskResponse>> legacyHandler)
    {
        _router.AsyncHandlers[taskType] = async (entity, payload, em) =>
        {
            try
            {
                var message = DeserializePayload(payload);
                var response = await legacyHandler(message, null, CancellationToken.None);
                return response.Success;
            }
            catch (Exception ex)
            {
                if (em.TryGetComponent<TaskStatusComponent>(entity, out var status))
                {
                    status.LastError = ex.Message;
                    em.SetComponent(entity, status);
                }
                return false;
            }
        };
    }

    public void RegisterSortMentHandler(string taskType,
        ISortMent<(Entity Entity, TaskPayloadComponent Payload, EntityManager EntityManager), bool> sortMent)
    {
        _router.RegisterHandler(taskType, sortMent);
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Update(float deltaTime)
    {
        if (_disposed) return;
        _world.Update(deltaTime);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }


    private static string SerializePayload(ITaskMessage task)
    {
        return System.Text.Json.JsonSerializer.Serialize(task);
    }

    private static ITaskMessage DeserializePayload(TaskPayloadComponent payload)
    {
        return payload.TaskType switch
        {
            "FileDrivenPipeline" => System.Text.Json.JsonSerializer.Deserialize<Messages.FileDrivenPipelineMessage>(payload.PayloadJson)
                ?? throw new InvalidOperationException("Failed to deserialize FileDrivenPipelineMessage"),
            "ExecuteCommand" => System.Text.Json.JsonSerializer.Deserialize<Messages.ExecuteCommandMessage>(payload.PayloadJson)
                ?? throw new InvalidOperationException("Failed to deserialize ExecuteCommandMessage"),
            "LoadConfig" => System.Text.Json.JsonSerializer.Deserialize<Messages.LoadConfigMessage>(payload.PayloadJson)
                ?? throw new InvalidOperationException("Failed to deserialize LoadConfigMessage"),
            "ExecuteFunction" => System.Text.Json.JsonSerializer.Deserialize<Messages.ExecuteFunctionMessage>(payload.PayloadJson)
                ?? throw new InvalidOperationException("Failed to deserialize ExecuteFunctionMessage"),
            _ => throw new NotSupportedException($"Unknown task type: {payload.TaskType}")
        };
    }
}