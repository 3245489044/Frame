@{
    # ===== SpecToolingContract 门禁基线（单源；守卫 = Operation\DSL\Check-SpecToolingContract.ps1）=====
    # 语义：AgentTest 规格工具链（Tools\**\*.ps1）的**结构事实下限**（可发现性 + 确定性退出 + 无深度计数）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'SpecToolingContract'

    # 正对照下限：扫描到的工具数须 ≥ 此值（防扫描面被掏空 ⇒ 判据无标的假绿）。
    MinTools = @{
        Value = 15
        Origin = 'Derived'
        DueWhen = ''
    }

    # 确定性 exit 豁免（存量债务棘轮）：逐件补 exit 后从本表删除（只减不增）。
    ExitExempt = @{
        Value = @(
            'Migrate-SpecPortFixtures.ps1'
            'New-AgentSpecSuite.ps1'
            'Get-AgentTestMethodBaseline.ps1'
            'Compare-SpecSemantic.ps1'
            'Expand-SpecSource.ps1'
        )
        Origin = 'Debt'
        DueWhen = '逐件补确定性 exit（`exit 0/1/2`）后从本表删除；新增工具不得入表'
    }
}
