using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalWorkflowFramework.Relations.Mounting;

/// <summary>
/// 四轴唯一组合点（设计单 §3.5）：MountId ↔ Relation ↔ Trigger ↔ Stage ↔ Projection。
/// <para>不重复 edge/src/tgt：这些事实只来自 <see cref="RelationOperation"/>。</para>
/// </summary>
public sealed record MountBinding(
    string MountId,
    string RelationOperation,
    IReadOnlyList<string> Triggers,
    string Stage,
    int Order,
    string Family,
    string Coalesce,
    string Posture,
    string? ProjectionId);

/// <summary>
/// 挂载绑定目录（运行期只读）。
/// <para>单一来源 = 声明面；当前阶段（M1a）用与 <c>Config/relations/mounts.ushar</c> 逐字段对齐的
/// C# 镜像表，待 <c>[Mount]</c> 语言单元落地后改由同一次编译产出（接口与本目录不变）。</para>
/// </summary>
public sealed class MountBindingCatalog
{
    private readonly Dictionary<string, MountBinding> _byId;
    private readonly Dictionary<string, MountBinding?> _byOperation;
    private readonly IReadOnlyList<MountBinding> _ordered;

    public MountBindingCatalog(IEnumerable<MountBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        _byId = new Dictionary<string, MountBinding>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (string.IsNullOrWhiteSpace(binding.MountId))
                throw new ArgumentException("挂载绑定 MountId 不能为空", nameof(bindings));
            if (!_byId.TryAdd(binding.MountId, binding))
                throw new InvalidOperationException($"挂载绑定重复声明：{binding.MountId}（单一实现原则）");
        }

        _ordered = _byId.Values.OrderBy(b => b.Order).ThenBy(b => b.MountId, StringComparer.Ordinal).ToList();

        // 反向索引（UMF · R2）：operation → 绑定；多义 ⇒ null（fail-closed，禁猜）。
        _byOperation = new Dictionary<string, MountBinding?>(StringComparer.Ordinal);
        foreach (var binding in _ordered)
        {
            if (string.IsNullOrWhiteSpace(binding.RelationOperation)) continue;
            _byOperation[binding.RelationOperation] = _byOperation.ContainsKey(binding.RelationOperation)
                ? null
                : binding;
        }
    }

    /// <summary>默认绑定（与设计单 §6.2 / 现有 <c>relation-ops.ushar</c> operation 名逐字对齐）。</summary>
    public static MountBindingCatalog Default { get; } = new(DefaultBindings());

    /// <summary>全部绑定（按 order 升序）。</summary>
    public IReadOnlyList<MountBinding> Bindings => _ordered;

    /// <summary>按 MountId 解析（未声明 ⇒ null）。</summary>
    public MountBinding? Resolve(string mountId)
        => !string.IsNullOrWhiteSpace(mountId) && _byId.TryGetValue(mountId, out var found) ? found : null;

    /// <summary>
    /// 按关系 operation 反查绑定（UMF · R2）。0 命中 / 多义 ⇒ <c>null</c>（fail-closed）。
    /// <para>重建投影只产出 operation，MountId 必须由本表反查；禁以 operation 名猜语义 id。</para>
    /// </summary>
    public MountBinding? ResolveByOperation(string relationOperation)
    {
        if (string.IsNullOrWhiteSpace(relationOperation)) return null;
        return _byOperation.TryGetValue(relationOperation, out var found) ? found : null;
    }

    /// <summary>按阶段 + 族 + 顺序解析（同阶段内固定顺序；0/多义 ⇒ null，fail-closed）。</summary>
    public MountBinding? ResolveFor(string stage, string family, int order)
    {
        var matches = _ordered
            .Where(b => string.Equals(b.Stage, stage, StringComparison.Ordinal)
                        && string.Equals(b.Family, family, StringComparison.Ordinal)
                        && b.Order == order)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>默认绑定表（单一字面量点；只引用 operation 名，不写 edge/端点）。</summary>
    public static IReadOnlyList<MountBinding> DefaultBindings() => new[]
    {
        new MountBinding(
            MountIds.NodeToTask,
            RelationOperation: "mount_node_task",
            Triggers: new[] { MountTriggers.NodePersisted, MountTriggers.RoundSettled, MountTriggers.CommandExplicit, MountTriggers.RebuildRequested },
            Stage: MountStages.Settle,
            Order: 10,
            Family: MountFamilies.Structural,
            Coalesce: "edge",
            Posture: MountPostures.Require,
            ProjectionId: "TaskNodes"),

        new MountBinding(
            MountIds.TaskToHistory,
            RelationOperation: "mount_task_history",
            Triggers: new[] { MountTriggers.TaskCreated, MountTriggers.CommandExplicit, MountTriggers.RebuildRequested },
            Stage: MountStages.Provision,
            Order: 20,
            Family: MountFamilies.Structural,
            Coalesce: "edge",
            Posture: MountPostures.Require,
            ProjectionId: "HistoryTasks"),

        new MountBinding(
            MountIds.MessageToSession,
            RelationOperation: "mount_session_node",
            Triggers: new[] { MountTriggers.RoundSettled, MountTriggers.RebuildRequested },
            Stage: MountStages.Settle,
            Order: 30,
            Family: MountFamilies.Presentation,
            Coalesce: "edge",
            Posture: MountPostures.Require,
            ProjectionId: "SessionNodes"),

        new MountBinding(
            MountIds.TaskTree,
            RelationOperation: "mount_subtask",
            Triggers: new[] { MountTriggers.CommandExplicit, MountTriggers.RebuildRequested },
            Stage: MountStages.Explicit,
            Order: 40,
            Family: MountFamilies.Structural,
            Coalesce: "edge",
            Posture: MountPostures.Require,
            ProjectionId: "TaskTree"),
    };
}
