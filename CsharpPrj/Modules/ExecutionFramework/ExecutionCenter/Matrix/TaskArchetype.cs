
using ECS.Core;
using ExecutionCenter.Matrix.Components;

namespace ExecutionCenter.Matrix;

public static class TaskArchetype
{
    public static Entity CreatePendingTask(
        EntityManager entityManager,
        string taskType,
        string payloadJson,
        int priority = 2,
        int maxRetries = 3)
    {
        ArgumentNullException.ThrowIfNull(entityManager);

        var entity = entityManager.CreateEntity();
        entityManager.AddComponent(entity, new TaskPayloadComponent(taskType, payloadJson));
        entityManager.AddComponent(entity, new TaskStatusComponent(maxRetries));
        entityManager.AddComponent(entity, new TaskPriorityComponent(priority));
        return entity;
    }

    public static Entity CreateRoutedTask(
        EntityManager entityManager,
        string taskType,
        string payloadJson,
        string handlerName,
        string targetEnvironment = "CLI",
        int priority = 2,
        int maxRetries = 3)
    {
        ArgumentNullException.ThrowIfNull(entityManager);

        var entity = CreatePendingTask(entityManager, taskType, payloadJson, priority, maxRetries);
        entityManager.AddComponent(entity, new TaskRoutingComponent(handlerName, targetEnvironment));
        return entity;
    }

    public static Entity CreateDependentTask(
        EntityManager entityManager,
        string taskType,
        string payloadJson,
        string handlerName,
        Entity[] dependencies,
        string targetEnvironment = "CLI",
        int priority = 2,
        int maxRetries = 3)
    {
        ArgumentNullException.ThrowIfNull(entityManager);

        var entity = CreateRoutedTask(entityManager, taskType, payloadJson, handlerName, targetEnvironment, priority, maxRetries);
        entityManager.AddComponent(entity, new TaskDependencyComponent
        {
            Dependencies = dependencies,
            AllDependenciesMet = false
        });
        return entity;
    }
}