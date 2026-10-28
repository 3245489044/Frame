@{
    # ===== 图库索引声明基线（纯数据单源；守卫 = Operation\DSL\Data\Check-GraphIndexDeclaration.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'GraphIndexDeclaration'
    Source = 'Frame\PowerShellScript\Operation\DSL\Data\Check-GraphIndexDeclaration.ps1'

    # EnsureIndex 调用数（棘轮：只降不升；含边身份唯一索引）。
    # 8 = SourceId/TargetId/RefType/RelationId/WorkflowId/ResourceType/Id/Kind；+1 = idx_graph_edge_identity(unique)。
    EnsureIndexCount = @{
        Value   = 9
        Origin  = 'Debt'
        DueWhen = '索引全部由 [Table] graph 声明派生后降至 0'
    }
}
