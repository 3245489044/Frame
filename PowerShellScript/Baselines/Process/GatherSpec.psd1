@{
    # ===== GatherSpec 门禁基线（单源；守卫 = Operation\DSL\Agent\Check-GatherSpec.ps1）=====
    # 正对照（防「声明被删而门禁恒绿」）：视图数下限；声明面删视图须同步本基线（评审可见）。
    Schema = 'gate-baseline-v1'
    Gate   = 'GatherSpec'

    MinViews = @{ Value = 3; Origin = 'Derived'; DueWhen = '视图数由 Config\gather\*.json 机械复算；新增视图只增不减' }
}
