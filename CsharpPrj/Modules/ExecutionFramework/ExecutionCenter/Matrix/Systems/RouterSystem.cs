
using BasicMethod.SortMent;
using ECS.Core;
using ExecutionCenter.Matrix.Components;

namespace ExecutionCenter.Matrix.Systems;

public class RouterSystem : SystemBase
{
    public delegate Task<bool> TaskHandlerAsync(Entity entity, TaskPayloadComponent payload, EntityManager entityManager);

    public ISortMentRegistry<string, (Entity Entity, TaskPayloadComponent Payload, EntityManager EntityManager), bool> HandlerRegistry { get; }
        = new SortMentRegistry<string, (Entity, TaskPayloadComponent, EntityManager), bool>();

    public Dictionary<string, TaskHandlerAsync> AsyncHandlers { get; } = new();

    public Action<string>? OnHandlerNotFound { get; set; }

    public override void Update(float deltaTime)
    {
        var entities = EntityManager.GetAllEntities();
        foreach (var entity in entities)
        {
            if (!EntityManager.TryGetComponent<TaskStatusComponent>(entity, out var status))
                continue;
            if (status.CurrentState != TaskStatusComponent.State.Running)
                continue;

            if (!EntityManager.TryGetComponent<TaskRoutingComponent>(entity, out var routing))
                continue;

            if (!EntityManager.TryGetComponent<TaskPayloadComponent>(entity, out var payload))
                continue;

            if (AsyncHandlers.TryGetValue(routing.HandlerName, out var asyncHandler))
            {
                _ = ExecuteHandlerAsync(entity, asyncHandler, payload, status);
            }
            else if (HandlerRegistry.TryGet(routing.HandlerName, out var syncSortMent))
            {
                _ = ExecuteSortMentHandlerAsync(entity, syncSortMent, payload, status);
            }
            else
            {
                OnHandlerNotFound?.Invoke(routing.HandlerName);

                status.CurrentState = TaskStatusComponent.State.Failed;
                status.LastError = $"Handler '{routing.HandlerName}' not registered";
                EntityManager.SetComponent(entity, status);
            }
        }
    }

    public void RegisterHandler(string handlerName, ISortMent<(Entity Entity, TaskPayloadComponent Payload, EntityManager EntityManager), bool> sortMent)
    {
        HandlerRegistry.RegisterOrUpdate(handlerName, sortMent);
    }

    private async Task ExecuteHandlerAsync(
        Entity entity,
        TaskHandlerAsync handler,
        TaskPayloadComponent payload,
        TaskStatusComponent status)
    {
        try
        {
            var success = await handler(entity, payload, EntityManager);

            if (EntityManager.TryGetComponent<TaskStatusComponent>(entity, out var currentStatus))
            {
                if (currentStatus.CurrentState == TaskStatusComponent.State.Running)
                {
                    currentStatus.CurrentState = success
                        ? TaskStatusComponent.State.Succeeded
                        : TaskStatusComponent.State.Failed;
                    if (!success && string.IsNullOrEmpty(currentStatus.LastError))
                        currentStatus.LastError = "Handler returned false";
                    EntityManager.SetComponent(entity, currentStatus);
                }
            }
        }
        catch (Exception ex)
        {
            if (EntityManager.TryGetComponent<TaskStatusComponent>(entity, out var errorStatus))
            {
                errorStatus.CurrentState = TaskStatusComponent.State.Failed;
                errorStatus.LastError = $"Handler threw: {ex.Message}";
                EntityManager.SetComponent(entity, errorStatus);
            }
        }
    }

    private async Task ExecuteSortMentHandlerAsync(
        Entity entity,
        ISortMent<(Entity, TaskPayloadComponent, EntityManager), bool> sortMent,
        TaskPayloadComponent payload,
        TaskStatusComponent status)
    {
        try
        {
            var input = (entity, payload, EntityManager);
            var success = await Task.Run(() => sortMent.Morph(input));

            if (EntityManager.TryGetComponent<TaskStatusComponent>(entity, out var currentStatus))
            {
                if (currentStatus.CurrentState == TaskStatusComponent.State.Running)
                {
                    currentStatus.CurrentState = success
                        ? TaskStatusComponent.State.Succeeded
                        : TaskStatusComponent.State.Failed;
                    if (!success && string.IsNullOrEmpty(currentStatus.LastError))
                        currentStatus.LastError = "SortMent handler returned false";
                    EntityManager.SetComponent(entity, currentStatus);
                }
            }
        }
        catch (Exception ex)
        {
            if (EntityManager.TryGetComponent<TaskStatusComponent>(entity, out var errorStatus))
            {
                errorStatus.CurrentState = TaskStatusComponent.State.Failed;
                errorStatus.LastError = $"SortMent handler threw: {ex.Message}";
                EntityManager.SetComponent(entity, errorStatus);
            }
        }
    }
}