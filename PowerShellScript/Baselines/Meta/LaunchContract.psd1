@{
    # ===== LaunchContract 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-LaunchContract.ps1）=====
    # 语义：每个产品恰一份 *.launch.psd1，字段完备，入口映射双向覆盖（契约 Entries ⇔ EnableMent\*.bat）。
    Schema = 'gate-baseline-v1'
    Gate   = 'LaunchContract'

    ContractDirRelative = @{ Value = 'Frame\PowerShellScript\Share\Launch\Launchers'; Origin = 'Derived'; DueWhen = '' }
    ContractFilter      = @{ Value = '*.launch.psd1'; Origin = 'Derived'; DueWhen = '' }
    EntryDirRelative    = @{ Value = 'EnableMent'; Origin = 'Derived'; DueWhen = '' }
    MinContracts        = @{ Value = 1; Origin = 'Derived'; DueWhen = '新增产品契约后由目录复算（正对照下限，防扫描面坍缩）' }
    RequiredExecutor    = @{ Value = 'Frame\PowerShellScript\Share\Launch\Invoke-LaunchEntry.ps1'; Origin = 'Derived'; DueWhen = '' }
}
