@{
    # ===== GatherPoints 门禁基线（单源；守卫 = Operation\DSL\Agent\Declaration\Check-GatherPoints.ps1）=====
    # 正对照（防「单表被清空而门禁恒绿」）：聚拢点数量下限；删除聚拢点须同步本基线（评审可见）。
    Schema    = 'gate-baseline-v1'
    Gate      = 'GatherPoints'
    MinPoints = @{ Value = 4; Origin = 'Derived'; DueWhen = '聚拢点由 AgentGatherPoints 单表机械复算；只增不减' }
}
