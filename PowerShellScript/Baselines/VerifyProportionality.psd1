@{
    # ===== VerifyProportionality 门禁基线（单源；守卫 = Operation\DSL\Check-VerifyProportionality.ps1）=====
    # 语义（过程层缺陷 P4）：验证面须 = 受影响工程/符号面（成比例），非「改 1 工程跑全矩阵」。
    #   度量 = 验证触及工程数 ÷ 改动涉及工程数（对齐修复文档 §3.2 「验证/改动比 ≤ 1.5」）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'VerifyProportionality'

    # 比例上限（裁决类阈值；不可由产物机械复算）。
    RatioMax = @{ Value = 1.5; Origin = 'Verdict'; DueWhen = 'H 批稳定后按实测重裁（收紧或放宽）；超界样例恒须报' }

    # 正对照下限：比例复算样本数须 ≥ 此值（防判据无标的假绿）。
    MinSamples = @{ Value = 2; Origin = 'Derived'; DueWhen = '' }
}
