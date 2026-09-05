using UniversalWorkflowFramework.Core.Models;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 三层实体统一定位（消除 taskKey / sessionName / historyId 混叠）。
/// </summary>
/// <param name="Kind">实体类型。</param>
/// <param name="Id">实体 Id（History → historyId；Task → taskKey；Node → nodeKey）。</param>
/// <param name="WorkflowId">工作流命名空间（仅 Node 需要，缺省 agent-round）。</param>
public readonly record struct EntityRef(EntityKind Kind, string Id, string? WorkflowId = null)
{
    /// <summary>默认工作流命名空间（与 UI 层 attachNode 缺省一致）。</summary>
    public const string DefaultWorkflowId = "agent-round";

    /// <summary>定位文本（观测/归因）：history:demo / task:x / wf.node。</summary>
    public string Display
        => Kind switch
        {
            EntityKind.History => $"history:{Id}",
            EntityKind.Task => $"task:{Id}",
            EntityKind.Node => $"{WorkflowId ?? DefaultWorkflowId}.{Id}",
            _ => $"{Kind}:{Id}",
        };

    /// <summary>构造时校验：Node 必须带 WorkflowId（缺省 <see cref="DefaultWorkflowId"/>）。</summary>
    public EntityRef Normalize()
        => Kind == EntityKind.Node
            ? this with { WorkflowId = string.IsNullOrWhiteSpace(WorkflowId) ? DefaultWorkflowId : WorkflowId }
            : this;

    /// <summary>映射到 LiteDB 图资源定位 <see cref="ResourceIdentifier"/>（图元数据为标识符状态真源）。</summary>
    public ResourceIdentifier ToResourceIdentifier()
    {
        var norm = Normalize();
        return norm.Kind switch
        {
            EntityKind.History => new ResourceIdentifier { ResourceType = "history", Id = norm.Id },
            EntityKind.Task => new ResourceIdentifier { ResourceType = "task", Id = norm.Id },
            // 节点 Id 形如 "wf.nodeKey"（对齐 NodeRelationFactory.NodeId(wf,node)=wf.node）。
            EntityKind.Node => new ResourceIdentifier { ResourceType = "node", Id = $"{norm.WorkflowId}.{norm.Id}" },
            _ => new ResourceIdentifier { ResourceType = norm.Kind.ToString().ToLowerInvariant(), Id = norm.Id },
        };
    }
}
