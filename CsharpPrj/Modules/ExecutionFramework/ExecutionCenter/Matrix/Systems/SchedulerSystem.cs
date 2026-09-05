
using ECS.Core;
using ExecutionCenter.Matrix.Components;
using Modules.StateMachine;

namespace ExecutionCenter.Matrix.Systems;

public class SchedulerSystem : SystemBase
{
    private readonly IConditionalStateMachine<ScheduleState> _scheduleStateMachine;

    public int ScheduledCount { get; private set; }

    public SchedulerSystem()
    {
        _scheduleStateMachine = new ConditionalStateMachine<ScheduleState>(ScheduleState.Idle);
    }

    public enum ScheduleState
    {
        Idle,
        Scheduling,
        Completed
    }

    public override void Update(float deltaTime)
    {
        var entities = EntityManager.GetAllEntities();
        int scheduledThisTick = 0;

        foreach (var entity in entities)
        {
            if (!EntityManager.TryGetComponent<TaskStatusComponent>(entity, out var status))
                continue;
            if (status.CurrentState != TaskStatusComponent.State.Pending)
                continue;

            var canTransition = _scheduleStateMachine.TryTransitionTo(ScheduleState.Scheduling);

            bool dependenciesMet = true;
            bool hasDeps = EntityManager.TryGetComponent<TaskDependencyComponent>(entity, out var deps);
            if (hasDeps)
            {
                if (!CheckDependencies(entity, deps))
                    dependenciesMet = false;
            }

            if (!dependenciesMet)
                continue;

            if (canTransition)
            {
                if (hasDeps)
                {
                    deps.AllDependenciesMet = true;
                    EntityManager.SetComponent(entity, deps);
                }

                status.CurrentState = TaskStatusComponent.State.Running;
                EntityManager.SetComponent(entity, status);

                scheduledThisTick++;
                _scheduleStateMachine.TryTransitionTo(ScheduleState.Completed);
                _scheduleStateMachine.TryTransitionTo(ScheduleState.Idle);
            }
        }

        if (scheduledThisTick > 0)
            ScheduledCount += scheduledThisTick;
    }

    private bool CheckDependencies(Entity entity, TaskDependencyComponent deps)
    {
        if (deps.Dependencies == null || deps.Dependencies.Length == 0)
            return true;

        foreach (var dep in deps.Dependencies)
        {
            if (!EntityManager.IsEntityValid(dep))
                return false;

            if (EntityManager.TryGetComponent<TaskStatusComponent>(dep, out var depStatus))
            {
                if (depStatus.CurrentState != TaskStatusComponent.State.Succeeded)
                    return false;
            }
            else
            {
                return false;
            }
        }
        return true;
    }
}