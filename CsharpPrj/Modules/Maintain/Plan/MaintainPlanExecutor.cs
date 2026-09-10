#nullable enable

using Modules.Maintain.Operations;
using Modules.Maintain.Ports;
using Modules.Maintain.Workspace;
using Share.LogicComposition.Models;

namespace Modules.Maintain.Plan
{
    /// <summary>
    /// 管线执行器：依次执行 <see cref="MaintainPlan{TData}"/> 中的各步骤，
    /// 委派给 <see cref="MaintainOperations{TData}"/>，并把每步结果登记到 <see cref="MaintainReport"/>。
    /// </summary>
    public sealed class MaintainPlanExecutor<TData>
    {
        /// <summary>
        /// 执行整条管线。需要 <paramref name="workspace"/> 与 <paramref name="identity"/>；
        /// <paramref name="refMap"/> / <paramref name="store"/> 可选（缺失时跳过对应操作副作用）。
        /// </summary>
        public MaintainReport Execute(
            IDataWorkspace<TData> workspace,
            IIdentityResolver<TData> identity,
            IReferenceMap<TData>? refMap,
            IMaintainStateStore<TData>? store,
            MaintainPlan<TData> plan)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var report = new MaintainReport();
            foreach (var step in plan.Steps)
            {
                switch (step.Kind)
                {
                    case MaintainOperationKind.Upsert:
                        MaintainOperations<TData>.Upsert(workspace, (TData)step.Payload!);
                        report.AddPair(MaintainOperationKind.Upsert, workspace.GetKey((TData)step.Payload!), null);
                        break;

                    case MaintainOperationKind.Remove:
                        var removedKey = (string)step.Payload!;
                        var removed = MaintainOperations<TData>.Remove(workspace, refMap, removedKey);
                        report.AddPair(MaintainOperationKind.Remove, removedKey, null,
                            removed ? null : "key not found");
                        break;

                    case MaintainOperationKind.Merge:
                        var handling = (DuplicateHandling)step.Payload!;
                        var merged = MaintainOperations<TData>.Merge(workspace, identity, handling);
                        report.AddPair(MaintainOperationKind.Merge, null, null,
                            $"reduced to {merged.Count}");
                        break;

                    case MaintainOperationKind.Prune:
                        var pruned = MaintainOperations<TData>.Prune(workspace);
                        report.AddPair(MaintainOperationKind.Prune, null, null,
                            $"pruned {pruned}");
                        break;

                    case MaintainOperationKind.Rename:
                        var (oldKey, newKey) = ((string, string))step.Payload!;
                        var (renamed, refsWritten) =
                            MaintainOperations<TData>.Rename(workspace, identity, refMap, oldKey, newKey);
                        report.AddPair(MaintainOperationKind.Rename, oldKey, newKey,
                            refsWritten > 0 ? $"refs rewritten: {refsWritten}" : null);
                        break;

                    case MaintainOperationKind.Remap:
                        var map = (IReadOnlyDictionary<string, string>)step.Payload!;
                        var (mapped, conflicting, _) =
                            MaintainOperations<TData>.Remap(workspace, identity, refMap, map);
                        report.AddPair(MaintainOperationKind.Remap, null, null,
                            $"mapped {mapped}, conflicts {conflicting}");
                        break;

                    case MaintainOperationKind.Snapshot:
                        var revision = (string)step.Payload!;
                        if (store != null)
                        {
                            var stateId = MaintainOperations<TData>.Snapshot(workspace, store, revision);
                            report.Add(new ChangeEntry(MaintainOperationKind.Snapshot,
                                NewKey: stateId, Message: revision));
                        }
                        break;

                    case MaintainOperationKind.Revert:
                        var stateId2 = (string)step.Payload!;
                        if (store != null)
                        {
                            var restored = MaintainOperations<TData>.Revert(workspace, store, stateId2);
                            report.AddPair(MaintainOperationKind.Revert, null, null,
                                restored != null ? $"restored {restored.Count} items" : "state not found");
                        }
                        break;
                }
            }
            return report;
        }
    }
}