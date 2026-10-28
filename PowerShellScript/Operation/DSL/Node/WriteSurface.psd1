@{
    # ===== 关系写面清单（单源；Gate=WriteSurface，守卫 = 同目录 Check-WriteSurface.ps1）=====
    # 判据载体 = 结构事实：文件 + 符号 + 声明字段；Status 只降 pending（棘轮见 WriteSurface.baseline.json）。
    # 落点说明：清单不属「门禁数值基线」（Baselines\*.psd1 受 U3/U9/U11 事实行形态约束），故与守卫同目录。
    Schema = 'relation-surface-v1'
    Surface = 'relation-write'
    DeclaredOps = 'Projects/Plug/Agent/Config/relations/relation-ops.ushar'

    Entries = @(
        @{ Id = 'W1.task.reconcile';       File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/TaskReferenceGrounding.cs'; Symbol = 'ReconcileTaskAsync';       Edge = 'cross_db_link';    Intent = 'ensure'; Operation = 'mount_node_task';      Kind = 'ownership'; Wave = 'W1'; Status = 'migrated' }
        @{ Id = 'W1.task.node-link';       File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/TaskReferenceGrounding.cs'; Symbol = 'EnsureTaskNodeLinkAsync';  Edge = 'cross_db_link';    Intent = 'ensure'; Operation = 'mount_node_task';      Kind = 'ownership'; Wave = 'W1'; Status = 'migrated' }
        @{ Id = 'W1.task.node-unlink';     File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/TaskReferenceGrounding.cs'; Symbol = 'UnlinkTaskNodeAsync';      Edge = 'cross_db_link';    Intent = 'remove'; Operation = 'unmount_node_task';    Kind = 'ownership'; Wave = 'W1'; Status = 'migrated' }
        @{ Id = 'W4.task.subtask';         File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/TaskReferenceGrounding.cs'; Symbol = 'LinkSubTaskAsync';         Edge = 'task_owns_task';   Intent = 'ensure'; Operation = 'mount_subtask';        Kind = 'ownership'; Wave = 'W4'; Status = 'migrated' }
        @{ Id = 'W4.task.subtask-unlink';  File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/TaskReferenceGrounding.cs'; Symbol = 'UnlinkSubTaskAsync';       Edge = 'task_owns_task';   Intent = 'remove'; Operation = 'unmount_subtask';      Kind = 'ownership'; Wave = 'W4'; Status = 'migrated' }
        @{ Id = 'W1.history.attach';       File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/ContainerReferenceGrounding.cs'; Symbol = 'AttachHistoryTaskAsync'; Edge = 'history_owns_task'; Intent = 'ensure'; Operation = 'mount_task_history';   Kind = 'ownership'; Wave = 'W1'; Status = 'migrated' }
        @{ Id = 'W1.history.detach';       File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/ContainerReferenceGrounding.cs'; Symbol = 'DetachHistoryTaskAsync'; Edge = 'history_owns_task'; Intent = 'remove'; Operation = 'unmount_task_history'; Kind = 'ownership'; Wave = 'W1'; Status = 'migrated' }
        @{ Id = 'W1.session.link';         File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/SessionReferenceGrounding.cs'; Symbol = 'AddSessionMessageLinkAsync'; Edge = 'session_owns'; Intent = 'ensure'; Operation = 'mount_session_node';   Kind = 'ownership'; Wave = 'W1'; Status = 'migrated' }

        # ---- 已迁移（W4/W5/W6）：数据流残留字段与 C4 白名单双向一致 ----
        @{ Id = 'W4.taskgraph.edges';      File = 'Frame/CsharpPrj/Operation/HierarchyMethod/Nodes/TaskGraph/TaskGraphService.cs';     Symbol = 'MaintainReferencesAsync'; Edge = 'cross_db_link'; Intent = 'ensure'; Operation = 'mount_node_task'; Kind = 'ownership'; Wave = 'W4'; Status = 'migrated'; DataflowResidual = $true }
        @{ Id = 'W5.gateway.refs';         File = 'Frame/CsharpPrj/Operation/LoopFoldMethod/Reference/Gateways/NodeRelationGateway.cs'; Symbol = 'AddReferenceAsync'; Edge = 'cross_db_link'; Intent = 'ensure'; Operation = 'mount_node_task'; Kind = 'ownership'; Wave = 'W5'; Status = 'migrated'; DataflowResidual = $true }
        @{ Id = 'W5.ui.refs';              File = 'Projects/Plug/Agent/src/Agent.Ui/Core/Functions/Data/AgentUiLibraryData.cs';       Symbol = 'MutateReferenceAsync'; Edge = '*'; Intent = '*'; Operation = '*'; Kind = 'ownership'; Wave = 'W5'; Status = 'migrated'; ForwardedVia = 'NodeRelationGateway' }
        @{ Id = 'W6.graph-edge-writer';    File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Core/Graph/GraphEdgeWriter.cs';           Symbol = 'AttachEdgeAsync'; Edge = '*'; Intent = '*'; Operation = '*'; Kind = 'ownership'; Wave = 'W6'; Status = 'whitelisted' }
        @{ Id = 'W6.endpoint-rekey';       File = 'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/EndpointKeyRekey.cs';   Symbol = 'ApplyAsync'; Edge = 'cross_db_link'; Intent = 'ensure'; Operation = 'mount_node_task'; Kind = 'ownership'; Wave = 'W6'; Status = 'migrated'; DataflowResidual = $true }
        # ---- D8：无旧写原语（经服务/适配层转发）⇒ migrated ----
        @{ Id = 'D8.sync-relations';       File = 'Projects/Plug/Agent/src/Agent.Core/Orchestration/SessionWorkflow/NodeWrite/SyncRelationsBlock.cs'; Symbol = '*'; Edge = '*'; Intent = '*'; Operation = '*'; Kind = 'ownership'; Wave = 'D8'; Status = 'migrated' }
        @{ Id = 'D8.task-provision';       File = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Provision/TaskProvisionOpsFunctions.cs'; Symbol = '*'; Edge = '*'; Intent = '*'; Operation = '*'; Kind = 'ownership'; Wave = 'D8'; Status = 'migrated' }
        @{ Id = 'D8.round-attribution';    File = 'Projects/Plug/Agent/src/Agent.Modules/Framework/RoundAttribution/RoundAttributionOpsFunctions.cs'; Symbol = '*'; Edge = '*'; Intent = '*'; Operation = '*'; Kind = 'ownership'; Wave = 'D8'; Status = 'migrated' }
        @{ Id = 'D8.subagent-history';     File = 'Projects/Plug/Agent/src/Agent.Modules/SubAgent/Runtime/SubAgentHistoryBinding.cs'; Symbol = '*'; Edge = '*'; Intent = '*'; Operation = '*'; Kind = 'ownership'; Wave = 'D8'; Status = 'migrated' }
        @{ Id = 'D8.ui-history';           File = 'Projects/Plug/Agent/src/Agent.Ui/Core/Functions/History/AgentUiLibraryHistory.cs';   Symbol = '*'; Edge = '*'; Intent = '*'; Operation = '*'; Kind = 'ownership'; Wave = 'D8'; Status = 'migrated' }
        @{ Id = 'D8.ui-history-ops';       File = 'Projects/Plug/Agent/src/Agent.Ui/Core/Functions/HistoryOps/AgentUiLibraryHistoryOps.cs'; Symbol = '*'; Edge = '*'; Intent = '*'; Operation = '*'; Kind = 'ownership'; Wave = 'D8'; Status = 'migrated' }
    )
}
