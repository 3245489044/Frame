using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Share.TaskStatusPool.TaskPool;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.HierarchyMethod.Tasks.TaskGraph;

/// <summary>
/// 组内依赖探针（C3 闭环）：读引用关系端口，命中成员间 <c>task_owns_task</c> 边即拒收成组。
/// <para>判据载体 = 引用图结构事实（Src/Target 均在成员集合 ∧ RefType=task_owns_task），
/// 不读执行者叙述（«axioms:SEP.JudgmentCarrierStructural»）。</para>
/// <para>fail-closed：端口为 null / 查询异常 ⇒ 视为存在依赖（禁止「读不到就放行」）。</para>
/// </summary>
public sealed class TaskGraphDependencyProbe : ITaskGroupDependencyProbe
{
    private readonly IResourceReferenceStore? _references;

    public TaskGraphDependencyProbe(IResourceReferenceStore? references) => _references = references;

    public async Task<bool> HasIntraGroupDependencyAsync(
        IReadOnlyList<string> memberKeys, CancellationToken ct = default)
    {
        if (_references is null) return true;                 // 端口未装配 ⇒ fail-closed
        if (memberKeys is null || memberKeys.Count < 2) return false;

        var members = new HashSet<string>(memberKeys, StringComparer.Ordinal);
        foreach (var key in memberKeys)
        {
            ct.ThrowIfCancellationRequested();
            var references = await _references.GetReferencesAsync(
                new ResourceIdentifier { ResourceType = ReferenceTypes.Task, Id = key },
                includeIncoming: true, ct).ConfigureAwait(false);

            foreach (var reference in references)
            {
                if (!string.Equals(reference.RefType, ReferenceTypes.TaskOwnsTask, StringComparison.Ordinal))
                    continue;

                var source = reference.Source?.Id;
                var target = reference.Target?.Id;
                if (source is not null && target is not null
                    && members.Contains(source) && members.Contains(target))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
