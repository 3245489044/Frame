@{
    # ===== InvokeEntrypoints 门禁基线（单源；守卫 = Operation\DSL\Check-InvokeEntrypoints.ps1）=====
    # 语义：Frame\Invoke 入口层的**结构事实下限**（防判据无标的假绿）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'InvokeEntrypoints'

    # 正对照下限：声明 `$target = Join-Path $PSScriptRoot '…'` 的转发件数须 ≥ 此值。
    MinForwarders = @{
        Value = 16
        Origin = 'Derived'
        DueWhen = ''
    }
}
