@{
    Schema = 'gate-baseline-v1'
    Gate   = 'EdgeSingleWriter'

    # 边 → 写者映射（结构事实；来源 = relation-ops.ushar 的 [Relation] intent=ensure + 实际写面方法坐标）。
    # Role=business ⇒ 参与「恰 1 写者」判据；Role=unwired ⇒ 已声明未接线（显式债务，不参与计数）。
    Edges = @(
        @{ Value = 'cross_db_link';       Operation = 'mount_node_task';    Writer = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Gather\Adapters\DomainRelationSurface.cs:EnsureNodeTaskAsync'; Role = 'business'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'history_owns_task';   Operation = 'mount_task_history'; Writer = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Gather\Adapters\DomainRelationSurface.cs:AttachTaskAsync';     Role = 'business'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'session_owns';        Operation = 'mount_session_node'; Writer = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Gather\Adapters\DomainRelationSurface.cs:AttachNodeAsync';     Role = 'business'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'task_owns_task';      Operation = 'mount_subtask';      Writer = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Gather\Adapters\DomainRelationSurface.cs:EnsureSubTaskAsync';  Role = 'business'; Origin = 'Derived'; DueWhen = '' }
        @{ Value = 'history_owns_window'; Operation = 'mount_window';       Writer = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Mounting\MountBindingCatalog.cs:MountBindingCatalog';                Role = 'unwired';  Origin = 'Debt';    DueWhen = 'R6：窗口写面接线后转 business 并纳入计数' }
    )
}
