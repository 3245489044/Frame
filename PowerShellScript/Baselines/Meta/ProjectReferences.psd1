@{
    # ===== ProjectReferences 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-ProjectReferences.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'ProjectReferences'

    # 正对照：引用扫描面（Frame + Projects）的工程数下限。解析面坍缩 ⇒ 判据恒真 ⇒ 必须拦。
    ScanProjects = @{ Value = 250; Origin = 'Derived'; DueWhen = '可由 Get-ReferenceScanProjects 复算；新增/删除子树时同步' }

    # 跨子树手写路径债务（只减不增）：$(Proj_*) 覆盖后应为 0
    CrossDomainDebt = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }
}
