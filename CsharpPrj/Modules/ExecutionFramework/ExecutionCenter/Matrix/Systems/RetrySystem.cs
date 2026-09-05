
using BasicMethod.DecisionEngine;
using ECS.Core;
using ExecutionCenter.Matrix.Components;

namespace ExecutionCenter.Matrix.Systems;

public class RetrySystem : SystemBase
{
    public Action<Entity, TaskPayloadComponent, TaskStatusComponent>? OnDeadLetter { get; set; }

    private readonly RuleBasedDecisionEngine<RetryDecision, RetryContext> _decisionEngine;

    public int RetriedCount { get; private set; }
    public int DeadLetterCount { get; private set; }

    public RetrySystem()
    {
        _decisionEngine = new RuleBasedDecisionEngine<RetryDecision, RetryContext>(
            RetryDecision.Retry, "RetryEngine");
        ConfigureDefaultRules();
    }

    public enum RetryDecision
    {
        Retry,
        DeadLetter
    }

    public record RetryContext(
        int RetryCount,
        int MaxRetries,
        string? LastError,
        int TotalPendingTasks);

    public RuleBasedDecisionEngine<RetryDecision, RetryContext> RuleEngine => _decisionEngine;

    private void ConfigureDefaultRules()
    {
        _decisionEngine.AddRule(new DecisionRule<RetryDecision, RetryContext>
        {
            Name = "RetryWithinLimit",
            FromState = RetryDecision.Retry,
            ToState = RetryDecision.Retry,
            ActionName = "Retry",
            Condition = ctx => ctx.RetryCount < ctx.MaxRetries,
            Priority = 10,
            Description = "重试次数未达上限，允许重试"
        });

        _decisionEngine.AddRule(new DecisionRule<RetryDecision, RetryContext>
        {
            Name = "RetryExceededToDeadLetter",
            FromState = RetryDecision.Retry,
            ToState = RetryDecision.DeadLetter,
            ActionName = "DeadLetter",
            Condition = ctx => ctx.RetryCount >= ctx.MaxRetries,
            Priority = 10,
            Description = "重试次数已达上限，进入死信"
        });
    }

    public override void Update(float deltaTime)
    {
        var entities = EntityManager.GetAllEntities();
        int totalPending = entities.Count(e =>
        {
            if (!EntityManager.TryGetComponent<TaskStatusComponent>(e, out var s))
                return false;
            return s.CurrentState == TaskStatusComponent.State.Pending;
        });

        foreach (var entity in entities)
        {
            if (!EntityManager.TryGetComponent<TaskStatusComponent>(entity, out var status))
                continue;
            if (status.CurrentState != TaskStatusComponent.State.Failed)
                continue;

            var context = new RetryContext(
                RetryCount: status.RetryCount,
                MaxRetries: status.MaxRetries,
                LastError: status.LastError,
                TotalPendingTasks: totalPending);

            var decision = _decisionEngine.DecideAsync(context).GetAwaiter().GetResult();

            if (decision.NextState == RetryDecision.Retry)
            {
                status.CurrentState = TaskStatusComponent.State.Pending;
                status.RetryCount++;
                status.LastError = null;
                EntityManager.SetComponent(entity, status);
                RetriedCount++;
            }
            else
            {
                status.CurrentState = TaskStatusComponent.State.Dead;
                EntityManager.SetComponent(entity, status);
                DeadLetterCount++;

                if (EntityManager.TryGetComponent<TaskPayloadComponent>(entity, out var payload))
                {
                    OnDeadLetter?.Invoke(entity, payload, status);
                }
            }
        }
    }
}