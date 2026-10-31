@{
    # ===== GatherClosure 门禁基线（单源；守卫 = Operation\DSL\Agent\Runtime\Check-GatherClosure.ps1）=====
    # 实测值 = 声明面落地当日的直呼数（G0 影子清单）；只降不升（棘轮）。
    # 收敛路径：G1c 调用方迁移 → G2 出口闭合 → G3 封口（目标 0）。
    Schema = 'gate-baseline-v1'
    Gate   = 'GatherClosure'

    MaxViolations = @{ Value = 27; Origin = 'Debt'; DueWhen = 'G1c/G3：直呼清单收敛后逐批下调（G0 实测 27），最终 0 并切 Enforce' }
}
