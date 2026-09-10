#nullable enable

using Share.LogicComposition.Models;

namespace Modules.Maintain.Plan
{
    /// <summary>管线中的一个步骤（操作种类 + 载荷）。</summary>
    internal sealed record PlanStep(MaintainOperationKind Kind, object? Payload);

    /// <summary>
    /// 声明式维护/迁移管线。用 fluent 方式串联多个独立、幂等、可组合的操作，
    /// 交给 <see cref="MaintainPlanExecutor{TData}"/> 依次执行并登记到 <see cref="MaintainReport"/>。
    /// </summary>
    /// <example>
    /// <code>
    /// var plan = new MaintainPlan&lt;T&gt;()
    ///     .Snapshot("pre")
    ///     .Remap(renameMap)
    ///     .Merge(DuplicateHandling.Skip)
    ///     .Prune();
    /// </code>
    /// </example>
    public sealed class MaintainPlan<TData>
    {
        private readonly List<PlanStep> _steps = new();

        internal IReadOnlyList<PlanStep> Steps => _steps;

        public MaintainPlan<TData> Upsert(TData item)
        { _steps.Add(new PlanStep(MaintainOperationKind.Upsert, item)); return this; }

        public MaintainPlan<TData> Remove(string key)
        { _steps.Add(new PlanStep(MaintainOperationKind.Remove, key)); return this; }

        public MaintainPlan<TData> Merge(DuplicateHandling handling)
        { _steps.Add(new PlanStep(MaintainOperationKind.Merge, handling)); return this; }

        public MaintainPlan<TData> Prune()
        { _steps.Add(new PlanStep(MaintainOperationKind.Prune, null)); return this; }

        public MaintainPlan<TData> Rename(string oldKey, string newKey)
        { _steps.Add(new PlanStep(MaintainOperationKind.Rename, (oldKey, newKey))); return this; }

        public MaintainPlan<TData> Remap(IReadOnlyDictionary<string, string> map)
        { _steps.Add(new PlanStep(MaintainOperationKind.Remap, map)); return this; }

        public MaintainPlan<TData> Snapshot(string revision)
        { _steps.Add(new PlanStep(MaintainOperationKind.Snapshot, revision)); return this; }

        public MaintainPlan<TData> Revert(string stateId)
        { _steps.Add(new PlanStep(MaintainOperationKind.Revert, stateId)); return this; }
    }
}