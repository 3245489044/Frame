namespace Share.ScopeFramework;

/// <summary>
/// S3 建 task→node 图边：确保节点归属任务（node_to_node 边，经 addReference 建）。设计 §4.3。
/// 前置：需要 task→node 边（addNode/linkNode 恒真；createTask 仅带 relatedNodes 时）。
/// 目标节点：createTask → RelatedNodes（原样作节点 id）；addNode/linkNode → {wf}.{name}（单节点）。
/// 两段式（决策点 D3）：守卫已装配 → addReference 建边（task→node）；守卫未装配 →
/// addNode/linkNode 仅写 ur_nodes 声明节点（保证节点归属），createTask 图边子项整体跳过——
/// 均记子项诊断 [skip]，不阻断其余步骤。权威：设计 §4.3 诊断契约（未装配返回诊断、不消耗额度）。
/// </summary>
internal sealed class EnsureTaskNodeEdgeStep : IStep
{
    public string Id => "S3";

    public bool CanExecute(ScopeContext scope)
    {
        if (string.Equals(scope.Cmd, "createTask", StringComparison.OrdinalIgnoreCase))
            return scope.RelatedNodes is { Count: > 0 };
        return true; // addNode / linkNode
    }

    public IReadOnlyList<ScopeAction> ProduceActions(ScopeContext scope, IScopeNamingRule naming)
    {
        var isCreate = string.Equals(scope.Cmd, "createTask", StringComparison.OrdinalIgnoreCase);

        // 目标节点 id 集合
        IEnumerable<string> targetIds = isCreate
            ? scope.RelatedNodes ?? Array.Empty<string>()
            : new[] { $"{naming.ResolveWorkflowId(scope)}.{scope.Name ?? string.Empty}" };

        // 守卫已装配：addReference 建 task→node 边（每目标一条）。
        if (scope.GraphGuardAssembled)
        {
            var list = new List<ScopeAction>();
            foreach (var id in targetIds.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                list.Add(new ScopeAction
                {
                    Action = "addReference",
                    SourceType = "task",
                    SourceId = scope.TaskKey,
                    TargetType = "node",
                    TargetId = id,
                    // 层内自治常量（零依赖豁免，A1）：Share.ScopeFramework 为零项目引用的纯逻辑框架，
                    // 物理上无法引用唯一源 Operation.DataStoreDriven.Abstractions.ReferenceTypes ⇒ 本层保留字面量，
                    // 由门禁 Check-ReferenceVocabulary 白名单登记（见其 §零依赖层豁免）。
                    RefType = "node_to_node",
                });
            }
            return list;
        }

        // 守卫未装配：addNode/linkNode 写 ur_nodes 声明节点（保证节点归属）；createTask 图边子项整体跳过（记诊断）。
        if (!isCreate)
        {
            var wf = naming.ResolveWorkflowId(scope);
            var name = scope.Name ?? string.Empty;
            var rid = $"{wf}.{name}";
            var nodeKey = $"relations.{wf}.{rid}";
            var declarationPayload = new
            {
                relationId = rid,
                // 层内自治常量（零依赖豁免，A1）：同上（Share.ScopeFramework 零项目引用）。
                refType = "node_to_node",
                source = new { resourceType = "task", id = scope.TaskKey },
                target = new { resourceType = "node", id = $"{wf}.{name}" },
            };
            return new[]
            {
                new ScopeAction
                {
                    Action = "node",
                    Op = "write",
                    NodeKey = nodeKey,
                    NodeType = "relation",
                    ThreadId = wf,
                    Payload = declarationPayload,
                }
            };
        }

        return Array.Empty<ScopeAction>(); // createTask + 守卫未装配：图边子项跳过，仅记诊断
    }

    /// <summary>守卫未装配时报告图边子项跳过（对齐设计 §4.3：未装配返回诊断、不阻断）。</summary>
    public IReadOnlyList<DiagnosticEntry> Diagnostics(ScopeContext scope)
    {
        if (scope.GraphGuardAssembled)
            return Array.Empty<DiagnosticEntry>();

        var isCreate = string.Equals(scope.Cmd, "createTask", StringComparison.OrdinalIgnoreCase);
        return isCreate
            ? new[] { new DiagnosticEntry(this, DiagnosticKind.Skip, "图守卫未装配，createTask 的 task→node 图边子项跳过（节点归属由入队写透守卫兜底）") }
            : new[] { new DiagnosticEntry(this, DiagnosticKind.Skip, "图守卫未装配，图边子项跳过（仅写 ur_nodes 声明节点，保证节点归属）") };
    }
}
